using System;
using GSM.Domain.Communications;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa13_4_EmailOutboxAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused")
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
                .OldAnnotation("Npgsql:Enum:communications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked")
                .OldAnnotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .OldAnnotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .OldAnnotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,transfer,card,other")
                .OldAnnotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,cardio");

            migrationBuilder.CreateTable(
                name: "email_outbox",
                schema: "communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: false),
                    text_body = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<EmailKind>(type: "communications.email_kind", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<EmailStatus>(type: "communications.email_status", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_communication_preferences",
                schema: "communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_opt_out = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_communication_preferences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reminder_days_before = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
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
                name: "ix_member_communication_preferences_member_id",
                schema: "communications",
                table: "member_communication_preferences",
                column: "member_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_settings_gym_id",
                schema: "communications",
                table: "settings",
                column: "gym_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_outbox",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "member_communication_preferences",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "communications");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
                .Annotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .Annotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership")
                .Annotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .Annotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .Annotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .Annotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived")
                .Annotation("Npgsql:Enum:communications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked")
                .Annotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused")
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
                .OldAnnotation("Npgsql:Enum:communications.email_kind", "announcement,class_cancelled,membership_expiring")
                .OldAnnotation("Npgsql:Enum:communications.email_status", "pending,sent,failed")
                .OldAnnotation("Npgsql:Enum:communications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked")
                .OldAnnotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .OldAnnotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .OldAnnotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,transfer,card,other")
                .OldAnnotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,cardio");
        }
    }
}
