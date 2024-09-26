using Microsoft.AspNetCore.SignalR;
using LectitioMendaciutatis.Data;
using LectitioMendaciutatis.Models;
using System.Threading.Tasks;
using System.Linq;

namespace LectitioMendaciutatis.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ChatContext _context;

        public ChatHub(ChatContext context)
        {
            _context = context;
        }

        // When a client connects, send the last 50 messages to them
        public override async Task OnConnectedAsync()
        {
            var messages = _context.ChatMessages
                .OrderBy(m => m.Timestamp)
                .Take(50)
                .ToList();

            foreach (var message in messages)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", message.Username, message.Message);
            }

            await base.OnConnectedAsync();
        }

        // Send a message to all connected clients
        public async Task SendMessage(string user, string message)
        {
            // Save the message to the database
            var chatMessage = new ChatMessage
            {
                Username = user,
                Message = message
            };

            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();

            // Broadcast the message to all clients
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}
