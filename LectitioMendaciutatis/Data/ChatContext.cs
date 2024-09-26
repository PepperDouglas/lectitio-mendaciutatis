using Microsoft.EntityFrameworkCore;
using LectitioMendaciutatis.Models;

namespace LectitioMendaciutatis.Data
{
    public class ChatContext : DbContext
    {
        public ChatContext(DbContextOptions<ChatContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
    }
}
