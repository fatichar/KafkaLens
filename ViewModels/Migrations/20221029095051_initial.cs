using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViewModels.Migrations
{
    public partial class initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kafkalens_client",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    protocol = table.Column<string>(type: "TEXT", nullable: false),
                    server_url = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kafkalens_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kafka_cluster",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    client_id = table.Column<string>(type: "TEXT", nullable: true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    bootstrap_servers = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kafka_cluster", x => x.id);
                    table.ForeignKey(
                        name: "FK_kafka_cluster_kafkalens_client_client_id",
                        column: x => x.client_id,
                        principalTable: "kafkalens_client",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_kafka_cluster_client_id",
                table: "kafka_cluster",
                column: "client_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kafka_cluster");

            migrationBuilder.DropTable(
                name: "kafkalens_client");
        }
    }
}
