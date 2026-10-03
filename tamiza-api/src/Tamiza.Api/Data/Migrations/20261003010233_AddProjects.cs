using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamiza.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "email_verified",
                schema: "tamiza",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "tamiza",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kobo_server_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    kobo_asset_uid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    encrypted_api_token = table.Column<string>(type: "text", nullable: false),
                    encrypted_webhook_secret = table.Column<string>(type: "text", nullable: false),
                    form_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    form_fields = table.Column<string>(type: "jsonb", nullable: false),
                    form_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enumerator_field = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "tamiza",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_invitations",
                schema: "tamiza",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_invitations", x => x.id);
                    table.CheckConstraint("ck_project_invitations_role", "role IN ('admin', 'analyst', 'viewer')");
                    table.ForeignKey(
                        name: "fk_project_invitations_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "tamiza",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_invitations_users_invited_by",
                        column: x => x.invited_by,
                        principalSchema: "tamiza",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                schema: "tamiza",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => new { x.project_id, x.user_id });
                    table.CheckConstraint("ck_project_members_role", "role IN ('admin', 'analyst', 'viewer')");
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "tamiza",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tamiza",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sampling_frames",
                schema: "tamiza",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimensions = table.Column<string>(type: "jsonb", nullable: false),
                    targets = table.Column<string>(type: "jsonb", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sampling_frames", x => x.project_id);
                    table.ForeignKey(
                        name: "fk_sampling_frames_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "tamiza",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sampling_frames_users_updated_by",
                        column: x => x.updated_by,
                        principalSchema: "tamiza",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_invited_by",
                schema: "tamiza",
                table: "project_invitations",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_normalized_email",
                schema: "tamiza",
                table: "project_invitations",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_project_id_normalized_email",
                schema: "tamiza",
                table: "project_invitations",
                columns: new[] { "project_id", "normalized_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_members_user_id",
                schema: "tamiza",
                table: "project_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_created_by",
                schema: "tamiza",
                table: "projects",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_sampling_frames_updated_by",
                schema: "tamiza",
                table: "sampling_frames",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_invitations",
                schema: "tamiza");

            migrationBuilder.DropTable(
                name: "project_members",
                schema: "tamiza");

            migrationBuilder.DropTable(
                name: "sampling_frames",
                schema: "tamiza");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "tamiza");

            migrationBuilder.DropColumn(
                name: "email_verified",
                schema: "tamiza",
                table: "users");
        }
    }
}
