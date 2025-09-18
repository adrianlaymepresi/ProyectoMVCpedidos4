using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PracticaPedidos4MVC.Migrations
{
    /// <inheritdoc />
    public partial class actualizarProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Categoria",
                table: "Products",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "Products");
        }
    }
}
