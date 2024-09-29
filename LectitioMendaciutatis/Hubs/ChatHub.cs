using Microsoft.AspNetCore.SignalR;
using LectitioMendaciutatis.Data;
using LectitioMendaciutatis.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LectitioMendaciutatis.Hubs
{
    //Ensure that only authenticated users can connect
    [Authorize] 
    public class ChatHub : Hub
    {
        private readonly ChatContext _context;
        private readonly string _aesKey;
        //Name matched with list of eligable names
        private static Dictionary<string, List<string>> privateRooms = new();

        public ChatHub(ChatContext context, IConfiguration configuration)
        {
            _context = context;
            _aesKey = configuration["AESKey"];
        }

        // When a client connects, send the last 50 messages to them
        public override async Task OnConnectedAsync()
        {
            var aesHelper = new AesEncryptionHelper(_aesKey);

            // Get the room name from the query string (passed by the client when connecting)
            var httpContext = Context.GetHttpContext();
            var roomName = httpContext.Request.Query["room"].ToString();
            // If no room is provided, default to "main"
            if (string.IsNullOrEmpty(roomName)) {
                roomName = "main";
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

        // Send a message to all connected clients
        public async Task SendMessage(string roomName, string user, string encryptedMessage)
        {
            if (Context.User?.Identity?.IsAuthenticated == true) {
                //Decrypt message
                var aesHelper = new AesEncryptionHelper(_aesKey);
                Console.WriteLine($"Received encrypted message: {encryptedMessage}");
                string decryptedMessage = aesHelper.Decrypt(encryptedMessage);
                Console.WriteLine($"Decrypted message: {decryptedMessage}");

                //Save the message to the database
                var chatMessage = new ChatMessage
                {
                    Username = user,
                    Message = decryptedMessage,
                    RoomName = roomName
                };

                _context.ChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                string encryptedResponseMessage = aesHelper.Encrypt(decryptedMessage);
                //Broadcast the message to all clients
                await Clients.All.SendAsync("ReceiveMessage", user, encryptedResponseMessage);
            } else {
                throw new HubException("You are not authorized to send messages.");
            }
        }

        public async Task CreateRoom()
        {
            var roomName = Context.User.Identity.Name; // Use creator's username as the room name
            if (!privateRooms.ContainsKey(roomName))
            {
                // Add room with the creator as the initial member
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
                return privateRooms[roomName];
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
            //Might be an issue with sqlite
            var user = await _context.Users.SingleOrDefaultAsync(u => u.Username == username);
            if (user != null)
            {
                privateRooms[roomName].Add(username);
                await Clients.Group(roomName).SendAsync("UserAdded", username);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "User does not exist.");
            }
        }

        public async Task RemoveUserFromRoom(string roomName, string username)
        {
            if (privateRooms.ContainsKey(roomName) && privateRooms[roomName].Contains(username))
            {
                privateRooms[roomName].Remove(username);
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
                .Select(room => room.Key); // Return room names (usernames) where the user is allowed

            await Clients.Caller.SendAsync("RoomsAvailable", availableRooms);
        }
    }
}
