using Microsoft.AspNetCore.SignalR;
using LectitioMendaciutatis.Data;
using LectitioMendaciutatis.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authorization;

namespace LectitioMendaciutatis.Hubs
{
    //Ensure that only authenticated users can connect
    [Authorize] 
    public class ChatHub : Hub
    {
        private readonly ChatContext _context;
        private readonly string _aesKey;

        public ChatHub(ChatContext context, IConfiguration configuration)
        {
            _context = context;
            _aesKey = configuration["AESKey"];
        }

        // When a client connects, send the last 50 messages to them
        public override async Task OnConnectedAsync()
        {
            var aesHelper = new AesEncryptionHelper(_aesKey);

            var messages = _context.ChatMessages
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
        public async Task SendMessage(string user, string encryptedMessage)
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
                    Message = decryptedMessage
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
    }
}
