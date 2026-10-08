using System;
using GSM.Domain.Notifications;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_N1_Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            // Los enums nativos se crean y se sueltan a mano (en lugar del AlterDatabase generado) para poder
            // copiar los datos entre las tablas viejas y las nuevas antes de soltar los tipos viejos.
            migrationBuilder.Sql("""
                CREATE TYPE notifications.notification_type AS ENUM ('class_cancelled', 'membership_expiring', 'achievement_unlocked', 'announcement', 'checkin_rejected');
                CREATE TYPE notifications.notification_category AS ENUM ('membership', 'classes', 'progress', 'news', 'promotions', 'security');
                CREATE TYPE notifications.notification_priority AS ENUM ('normal', 'urgent');
                CREATE TYPE notifications.notification_status AS ENUM ('pending', 'held', 'sent', 'failed', 'cancelled', 'skipped');
                CREATE TYPE notifications.delivery_channel AS ENUM ('push', 'email');
                CREATE TYPE notifications.delivery_status AS ENUM ('pending', 'sent', 'failed');
                """);

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<NotificationType>(type: "notifications.notification_type", nullable: false),
                    category = table.Column<NotificationCategory>(type: "notifications.notification_category", nullable: true),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    priority = table.Column<NotificationPriority>(type: "notifications.notification_priority", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<NotificationStatus>(type: "notifications.notification_status", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requires_action = table.Column<bool>(type: "boolean", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_recipient", "(recipient_user_id IS NULL OR recipient_member_id IS NULL) AND (requires_action OR recipient_user_id IS NOT NULL OR recipient_member_id IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<DeliveryChannel>(type: "notifications.delivery_channel", nullable: false),
                    status = table.Column<DeliveryStatus>(type: "notifications.delivery_status", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: false),
                    text_body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_notification_id_channel",
                schema: "notifications",
                table: "notification_deliveries",
                columns: new[] { "notification_id", "channel" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_status_next_attempt_at",
                schema: "notifications",
                table: "notification_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                schema: "notifications",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_gym_id_requires_action_resolved_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "gym_id", "requires_action", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_member_id_read_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_member_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_scheduled_for",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "status", "scheduled_for" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_type_recipient_user_id_recipient_member_id_re",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "type", "recipient_user_id", "recipient_member_id", "reference_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        
            // Copia la bandeja del miembro, la cola de correos y los intentos de entrada sin atender
            // (V2_N1.sql) y recién entonces suelta las tablas y los tipos viejos.
            migrationBuilder.SqlScript("N1");

            migrationBuilder.DropTable(
                name: "email_outbox",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "member_notifications",
                schema: "communications");

            migrationBuilder.Sql("""
                DROP TYPE communications.email_kind;
                DROP TYPE communications.email_status;
                DROP TYPE communications.notification_type;
                """);
        }

        /// <inheritdoc />

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_deliveries",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
                .Annotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .Annotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership")
                .Annotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .Annotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .Annotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .Annotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived")
                .Annotation("Npgsql:Enum:communications.email_kind", "announcement,class_cancelled,membership_expiring")
                .Annotation("Npgsql:Enum:communications.email_status", "pending,sent,failed")
                .Annotation("Npgsql:Enum:communications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked")
                .Annotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,transfer,card,other")
                .Annotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .Annotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,cardio")
                .OldAnnotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes")
                .OldAnnotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
                .OldAnnotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .OldAnnotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership")
                .OldAnnotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .OldAnnotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .OldAnnotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .OldAnnotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived")
                .OldAnnotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .OldAnnotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .OldAnnotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed")
                .OldAnnotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .OldAnnotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .OldAnnotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .OldAnnotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,transfer,card,other")
                .OldAnnotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,cardio");

            migrationBuilder.CreateTable(
                name: "email_outbox",
                schema: "communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<int>(type: "communications.email_kind", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "communications.email_status", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text_body = table.Column<string>(type: "text", nullable: false),
                    to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_notifications",
                schema: "communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    type = table.Column<int>(type: "communications.notification_type", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_kind_reference_id",
                schema: "communications",
                table: "email_outbox",
                columns: new[] { "kind", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_member_id_kind_reference_id",
                schema: "communications",
                table: "email_outbox",
                columns: new[] { "member_id", "kind", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_status_created_at",
                schema: "communications",
                table: "email_outbox",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_member_notifications_created_at",
                schema: "communications",
                table: "member_notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_member_notifications_member_id_read_at",
                schema: "communications",
                table: "member_notifications",
                columns: new[] { "member_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_member_notifications_member_id_type_reference_id",
                schema: "communications",
                table: "member_notifications",
                columns: new[] { "member_id", "type", "reference_id" },
                unique: true);
        }
    }
}
