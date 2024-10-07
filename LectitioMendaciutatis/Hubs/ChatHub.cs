using Ganss.Xss;
using LectitioMendaciutatis.Data;
using LectitioMendaciutatis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LectitioMendaciutatis.Hubs
{
    //Ensure that only authenticated users can connect
    [Authorize] 
    public class ChatHub : Hub
    {
        private readonly ChatContext _context;
        private readonly string _aesKey;
        private static Dictionary<string, List<string>> privateRooms = new();
        private readonly HtmlSanitizer _htmlSanitizer;
        private readonly ILogger<ChatHub> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ChatHub(ChatContext context, IConfiguration configuration, ILogger<ChatHub> logger, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _aesKey = configuration["AESKey"];
            _htmlSanitizer = new HtmlSanitizer();
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public override async Task OnConnectedAsync()
        {
            if (Context.User == null) {
                return;
            }
            var username = Context.User.Identity.Name;
            _logger.LogInformation($"User connected: {username}");
            _logger.LogInformation("User connected at {Time} with ConnectionId {ConnectionId}", DateTime.UtcNow, Context.ConnectionId);
            var aesHelper = new AesEncryptionHelper(_aesKey);

            var httpContext = _httpContextAccessor.HttpContext;
            var roomName = httpContext.Request.Query["room"].ToString();

            if (string.IsNullOrEmpty(roomName)) {
                roomName = "main";
            }

            if (!CanUserJoinRoom(roomName, username)) {
                await RemoveUserFromRoom(roomName, username);
                await Clients.Caller.SendAsync("Error", "You are not allowed to join this room.");
                Context.Abort();
                return;
            }

            var messages = _context.ChatMessages
                .Where(m => m.RoomName == roomName)
                .OrderBy(m => m.Timestamp)
                .Take(50)
                .ToList();

            foreach (var message in messages)
            {
                string encryptedMessage = aesHelper.Encrypt(message.Message);
                await Clients.Caller.SendAsync("ReceiveMessage", message.Username, encryptedMessage);
            }

            await base.OnConnectedAsync();
        }

        //Send a message to all connected clients
        public async Task SendMessage(string roomName, string user, string encryptedMessage)
        {
            if (Context.User?.Identity?.IsAuthenticated == true) {
                //Decrypt message
                var aesHelper = new AesEncryptionHelper(_aesKey);
                string decryptedMessage = aesHelper.Decrypt(encryptedMessage);

                //Sanitize message
                string sanitizedMessage = _htmlSanitizer.Sanitize(decryptedMessage);

                if (sanitizedMessage != decryptedMessage) {
                    _logger.LogWarning("Message from {User} was sanitized. Potential XSS detected.", user);
                }

                //Save the message to the database
                var chatMessage = new ChatMessage
                {
                    Username = user,
                    Message = sanitizedMessage,
                    RoomName = roomName
                };

                try {
                    _context.ChatMessages.Add(chatMessage);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Message from {User} in room {RoomName} was successfully saved at {Time}.", user, roomName, DateTime.UtcNow);

                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to save message from {User} in room {RoomName}.", user, roomName);
                    throw new HubException("Could not persist your message");
                }

                string encryptedResponseMessage = aesHelper.Encrypt(sanitizedMessage);
                //Broadcast the message to all clients
                await Clients.All.SendAsync("ReceiveMessage", user, encryptedResponseMessage);
            } else {
                throw new HubException("You are not authorized to send messages.");
            }
        }

        public async Task CreateRoom()
        {
            var roomName = Context.User.Identity.Name;
            if (!privateRooms.ContainsKey(roomName))
            {
                privateRooms.Add(roomName, new List<string> { Context.User.Identity.Name });
                await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
                await Clients.Caller.SendAsync("RoomCreated", roomName);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Room already exists.");
            }
        }

        //Return eligible users for a given room
        public List<string> GetEligibleUsers(string roomName) {
            if (privateRooms.ContainsKey(roomName)) {
                return privateRooms[roomName]
                .Where(username => username != roomName)
                .ToList();
            } else {
                throw new HubException("Room does not exist.");
            }
        }

        public async Task AddUserToRoom(string roomName, string username)
        {
            if (!privateRooms.ContainsKey(roomName))
            {
                await Clients.Caller.SendAsync("Error", "Room does not exist.");
                return;
            }
            var user = await _context.Users.SingleOrDefaultAsync(u => u.Username == username);
            if (user != null)
            {
                privateRooms[roomName].Add(username);
                await Clients.Group(roomName).SendAsync("UserAdded", username);
            }
            else
            {
                _logger.LogWarning("Attempted to add non-existent user {Username} to room {RoomName}.", username, roomName);
                await Clients.Caller.SendAsync("Error", "User does not exist.");
            }
        }

        public async Task RemoveUserFromRoom(string roomName, string username)
        {
            if (privateRooms.ContainsKey(roomName) && privateRooms[roomName].Contains(username))
            {
                privateRooms[roomName].Remove(username);
                await Clients.All.SendAsync("RemovedFromRoom", username);
                _logger.LogInformation($"Sent 'RemovedFromRoom' message to user {username}.");
                await Clients.Group(roomName).SendAsync("UserRemoved", username);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "User not in the room.");
            }
        }

        public async Task SearchRooms(string username)
        {
            var availableRooms = privateRooms
                .Where(room => room.Value.Contains(username))
                .Select(room => room.Key);

            await Clients.Caller.SendAsync("RoomsAvailable", availableRooms);
        }

        public bool CanUserJoinRoom(string roomName, string username) {
            if (roomName == "main" || roomName == username) {
                return true;
            }

            if (!privateRooms.ContainsKey(roomName)) {
                return false;
            }

            var eligibleUsers = GetEligibleUsers(roomName);
            return eligibleUsers.Contains(username);
        }

        public static bool DoesRoomExist(string roomName) {
            return privateRooms.ContainsKey(roomName);
        }

        public override async Task OnDisconnectedAsync(Exception exception) {
            _logger.LogInformation("User disconnected at {Time} with ConnectionId {ConnectionId}", DateTime.UtcNow, Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
