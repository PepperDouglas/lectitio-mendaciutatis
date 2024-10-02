namespace LectitioMendaciutatis.Tests;

using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using LectitioMendaciutatis.Hubs;
using LectitioMendaciutatis.Models;
using LectitioMendaciutatis.Data;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using System.Reflection;

public class ChatHubTests
{
    private readonly Mock<ILogger<ChatHub>> _loggerMock;
    private readonly ChatContext _context;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IHubCallerClients> _clientsMock;
    private readonly Mock<ISingleClientProxy> _callerMock;
    private readonly Mock<HubCallerContext> _hubCallerContextMock;
    private readonly ChatHub _chatHub;
    private readonly string _aesKey;

    public ChatHubTests() {
        // Generate a consistent AES key for all tests
        _aesKey = Convert.ToBase64String(new byte[32]); // 256-bit key

        // Mock Configuration
        _configMock = new Mock<IConfiguration>();
        _configMock.Setup(c => c["AESKey"]).Returns(_aesKey);

        // Mock Logger
        _loggerMock = new Mock<ILogger<ChatHub>>();

        // Setup in-memory database with unique name per test class instance
        var options = new DbContextOptionsBuilder<ChatContext>()
            .UseInMemoryDatabase(databaseName: "ChatHubTestDb_" + Guid.NewGuid())
            .Options;

        _context = new ChatContext(options);

        // Mock IHttpContextAccessor and HttpContext
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var mockHttpContext = new Mock<HttpContext>();
        _httpContextAccessorMock.Setup(accessor => accessor.HttpContext).Returns(mockHttpContext.Object);

        // Mock Clients
        _clientsMock = new Mock<IHubCallerClients>();
        _callerMock = new Mock<ISingleClientProxy>();
        _clientsMock.Setup(clients => clients.Caller).Returns(_callerMock.Object);

        // Mock HubCallerContext
        _hubCallerContextMock = new Mock<HubCallerContext>();
        _hubCallerContextMock.Setup(c => c.ConnectionId).Returns("TestConnectionId");

        // Set up default user identity
        var mockIdentity = new Mock<ClaimsIdentity>();
        mockIdentity.Setup(i => i.IsAuthenticated).Returns(true);
        mockIdentity.Setup(i => i.Name).Returns("DefaultTestUser");

        var mockPrincipal = new Mock<ClaimsPrincipal>();
        mockPrincipal.Setup(p => p.Identity).Returns(mockIdentity.Object);

        _hubCallerContextMock.Setup(c => c.User).Returns(mockPrincipal.Object);

        // Initialize ChatHub with mocked dependencies
        _chatHub = new ChatHub(_context, _configMock.Object, _loggerMock.Object, _httpContextAccessorMock.Object)
        {
            Clients = _clientsMock.Object,
            Context = _hubCallerContextMock.Object
        };
    }

    [Fact]
    public async Task OnConnectedAsync_Should_Retrieve_Last_50_Messages_And_Send_To_Client() {
        // Arrange
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            { "room", "main" }
        });

        // Set up the query string in the mock HttpContext
        var mockHttpContext = _httpContextAccessorMock.Object.HttpContext;
        Mock.Get(mockHttpContext).Setup(c => c.Request.Query).Returns(query);

        // Seed the database with 50 messages
        for (int i = 0; i < 50; i++) {
            _context.ChatMessages.Add(new ChatMessage
            {
                Username = "User" + i,
                Message = "Message " + i,
                RoomName = "main",
                Timestamp = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _context.SaveChangesAsync();

        // Create a helper to encrypt the messages, just like in the hub
        var aesHelper = new AesEncryptionHelper(_aesKey);

        // Act
        await _chatHub.OnConnectedAsync();

        // Assert
        for (int i = 0; i < 50; i++) {
            var expectedUsername = "User" + i;
            var expectedMessage = "Message " + i;

            // Encrypt the message using the same logic as the hub
            var expectedEncryptedMessage = aesHelper.Encrypt(expectedMessage);

            _callerMock.Verify(
                caller => caller.SendCoreAsync(
                    "ReceiveMessage",
                    It.Is<object[]>(o =>
                        (string)o[0] == expectedUsername &&
                        (string)o[1] == expectedEncryptedMessage),
                    default),
                Times.Once); // Each message should be sent exactly once
        }
    }

    [Fact]
    public async Task SendMessage_Should_Save_Message_To_Database() {
        // Arrange
        var testUsername = "TestUser";
        var testMessage = "Test Message";
        var testRoomName = "main";
        var aesKey = _aesKey; // Use the AES key from the test class

        // Mock the user identity and authentication
        var mockIdentity = new Mock<ClaimsIdentity>();
        mockIdentity.Setup(i => i.IsAuthenticated).Returns(true);
        mockIdentity.Setup(i => i.Name).Returns(testUsername);

        var mockPrincipal = new Mock<ClaimsPrincipal>();
        mockPrincipal.Setup(p => p.Identity).Returns(mockIdentity.Object);

        _hubCallerContextMock.Setup(c => c.User).Returns(mockPrincipal.Object);
        _hubCallerContextMock.Setup(c => c.ConnectionId).Returns("TestConnectionId");

        // Encrypt the message using the AesEncryptionHelper
        var aesHelper = new AesEncryptionHelper(aesKey);
        var encryptedMessage = aesHelper.Encrypt(testMessage);

        // Mock Clients.All
        var allClientsMock = new Mock<IClientProxy>();
        _clientsMock.Setup(clients => clients.All).Returns(allClientsMock.Object);

        // Act
        await _chatHub.SendMessage(testRoomName, testUsername, encryptedMessage);

        // Assert
        // Verify that the message was saved to the database
        var savedMessage = _context.ChatMessages.FirstOrDefault(m => m.Username == testUsername && m.Message == testMessage);
        Assert.NotNull(savedMessage);
        Assert.Equal(testUsername, savedMessage.Username);
        Assert.Equal(testMessage, savedMessage.Message);
        Assert.Equal(testRoomName, savedMessage.RoomName);

        // Verify that Clients.All.SendAsync was called with the correct parameters
        var expectedEncryptedResponseMessage = aesHelper.Encrypt(savedMessage.Message);
        allClientsMock.Verify(
            client => client.SendCoreAsync(
                "ReceiveMessage",
                It.Is<object[]>(o => (string)o[0] == testUsername && (string)o[1] == expectedEncryptedResponseMessage),
                default),
            Times.Once);
    }

    [Fact]
    public async Task SendMessage_Should_Broadcast_Message_To_All_Clients() {
        // Arrange
        var testUsername = "TestUser";
        var testMessage = "Test Message";
        var testRoomName = "main";
        var aesKey = _aesKey; // Use the AES key from the test class

        // Mock the user identity and authentication
        var mockIdentity = new Mock<ClaimsIdentity>();
        mockIdentity.Setup(i => i.IsAuthenticated).Returns(true);
        mockIdentity.Setup(i => i.Name).Returns(testUsername);

        var mockPrincipal = new Mock<ClaimsPrincipal>();
        mockPrincipal.Setup(p => p.Identity).Returns(mockIdentity.Object);

        _hubCallerContextMock.Setup(c => c.User).Returns(mockPrincipal.Object);
        _hubCallerContextMock.Setup(c => c.ConnectionId).Returns("TestConnectionId");

        // Encrypt the message using the AesEncryptionHelper
        var aesHelper = new AesEncryptionHelper(aesKey);
        var encryptedMessage = aesHelper.Encrypt(testMessage);

        // Mock Clients.All
        var allClientsMock = new Mock<IClientProxy>();
        _clientsMock.Setup(clients => clients.All).Returns(allClientsMock.Object);

        // Act
        await _chatHub.SendMessage(testRoomName, testUsername, encryptedMessage);

        // Assert
        // Verify that Clients.All.SendAsync was called with the correct parameters
        var expectedEncryptedResponseMessage = aesHelper.Encrypt(testMessage);
        allClientsMock.Verify(
            client => client.SendCoreAsync(
                "ReceiveMessage",
                It.Is<object[]>(o => (string)o[0] == testUsername && (string)o[1] == expectedEncryptedResponseMessage),
                default),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_Should_Send_Messages_From_Specific_Room() {
        // Arrange
        var roomName = "room1";
        var username = "DefaultTestUser";

        // Set up the query string in the mock HttpContext
        var query = new QueryCollection(new Dictionary<string, StringValues>
    {
        { "room", roomName }
    });
        var mockHttpContext = _httpContextAccessorMock.Object.HttpContext;
        Mock.Get(mockHttpContext).Setup(c => c.Request.Query).Returns(query);

        // Use reflection to access the private 'privateRooms' field
        var privateRoomsField = typeof(ChatHub).GetField("privateRooms", BindingFlags.NonPublic | BindingFlags.Static);
        var privateRooms = (Dictionary<string, List<string>>)privateRoomsField.GetValue(null);

        // Ensure the room exists and the user is eligible
        privateRooms[roomName] = new List<string> { username };

        // Seed the database with messages from multiple rooms
        for (int i = 0; i < 50; i++) {
            _context.ChatMessages.Add(new ChatMessage
            {
                Username = "User" + i,
                Message = "Message " + i,
                RoomName = (i % 2 == 0) ? "room1" : "room2",
                Timestamp = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _context.SaveChangesAsync();

        // Create a helper to encrypt the messages, just like in the hub
        var aesHelper = new AesEncryptionHelper(_aesKey);

        // Act
        await _chatHub.OnConnectedAsync();

        // Assert
        // Verify that only messages from 'room1' are sent to the user
        var messagesFromRoom1 = _context.ChatMessages
            .Where(m => m.RoomName == roomName)
            .OrderBy(m => m.Timestamp)
            .Take(50)
            .ToList();

        foreach (var message in messagesFromRoom1) {
            var expectedEncryptedMessage = aesHelper.Encrypt(message.Message);

            _callerMock.Verify(
                caller => caller.SendCoreAsync(
                    "ReceiveMessage",
                    It.Is<object[]>(o =>
                        (string)o[0] == message.Username &&
                        (string)o[1] == expectedEncryptedMessage),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // Ensure that messages from 'room2' were not sent
        var messagesFromRoom2 = _context.ChatMessages
            .Where(m => m.RoomName == "room2")
            .OrderBy(m => m.Timestamp)
            .Take(50)
            .ToList();

        foreach (var message in messagesFromRoom2) {
            var expectedEncryptedMessage = aesHelper.Encrypt(message.Message);

            _callerMock.Verify(
                caller => caller.SendCoreAsync(
                    "ReceiveMessage",
                    It.Is<object[]>(o =>
                        (string)o[0] == message.Username &&
                        (string)o[1] == expectedEncryptedMessage),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Fact]
    public async Task User_Cannot_Join_Private_Room_When_Not_In_Eligible_Users_List() {
        // Arrange
        var roomName = "PrivateRoom";
        var username = "UnauthorizedUser";

        // Set up user identity
        var mockIdentity = new Mock<ClaimsIdentity>();
        mockIdentity.Setup(i => i.IsAuthenticated).Returns(true);
        mockIdentity.Setup(i => i.Name).Returns(username);

        var mockPrincipal = new Mock<ClaimsPrincipal>();
        mockPrincipal.Setup(p => p.Identity).Returns(mockIdentity.Object);

        _hubCallerContextMock.Setup(c => c.User).Returns(mockPrincipal.Object);

        // Set up the query string in the mock HttpContext
        var query = new QueryCollection(new Dictionary<string, StringValues>
    {
        { "room", roomName }
    });
        var mockHttpContext = _httpContextAccessorMock.Object.HttpContext;
        Mock.Get(mockHttpContext).Setup(c => c.Request.Query).Returns(query);

        // Add room and eligible users (excluding the unauthorized user)
        var privateRoomsField = typeof(ChatHub).GetField("privateRooms", BindingFlags.NonPublic | BindingFlags.Static);
        var privateRooms = (Dictionary<string, List<string>>)privateRoomsField.GetValue(null);
        privateRooms[roomName] = new List<string> { "AuthorizedUser1", "AuthorizedUser2" };

        // Set up the Groups mock BEFORE calling OnConnectedAsync
        var groupsMock = new Mock<IGroupManager>();
        _chatHub.Groups = groupsMock.Object;

        // Act
        await _chatHub.OnConnectedAsync();

        // Assert
        // Verify that an error message was sent
        _callerMock.Verify(
            caller => caller.SendCoreAsync(
                "Error",
                It.Is<object[]>(o => o[0].ToString() == "You are not allowed to join this room."),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify that the user was not added to the group
        groupsMock.Verify(
            groups => groups.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
