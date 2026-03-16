using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TRVisionAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CameraIp = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    CameraModel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NgCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Frames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    FrameNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Verdict = table.Column<int>(type: "INTEGER", nullable: false),
                    SolutionName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TotalCount = table.Column<long>(type: "INTEGER", nullable: false),
                    NgCount = table.Column<long>(type: "INTEGER", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    MaskImagePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ApiSentAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Frames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Frames_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FrameId = table.Column<long>(type: "INTEGER", nullable: false),
                    ModuleName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Verdict = table.Column<int>(type: "INTEGER", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Modules_Frames_FrameId",
                        column: x => x.FrameId,
                        principalTable: "Frames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Frames_ApiSentAt",
                table: "Frames",
                column: "ApiSentAt");

            migrationBuilder.CreateIndex(
                name: "IX_Frames_ReceivedAt",
                table: "Frames",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Frames_SessionId",
                table: "Frames",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Modules_FrameId",
                table: "Modules",
                column: "FrameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Modules");

            migrationBuilder.DropTable(
                name: "Frames");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
