using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LectitioMendaciutatis.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomNameChatMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoomName",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoomName",
                table: "ChatMessages");
        }
    }
}
