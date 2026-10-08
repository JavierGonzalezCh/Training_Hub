using System;
using GSM.Domain.Members;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_M1_MemberProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_members_gym_id_document_number",
                schema: "members",
                table: "members");

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
                .Annotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .Annotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .Annotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .Annotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .Annotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .Annotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
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
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .OldAnnotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,cardio");

            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "members",
                table: "members",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "members",
                table: "members",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DocumentType>(
                name: "document_type",
                schema: "members",
                table: "members",
                type: "members.document_type",
                nullable: false,
                defaultValue: DocumentType.Cc);

            migrationBuilder.AddColumn<string>(
                name: "emergency_contact_name",
                schema: "members",
                table: "members",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "emergency_contact_phone",
                schema: "members",
                table: "members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "emergency_contact_relationship",
                schema: "members",
                table: "members",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Gender>(
                name: "gender",
                schema: "members",
                table: "members",
                type: "members.gender",
                nullable: true);

            migrationBuilder.AddColumn<MemberGoal>(
                name: "goal",
                schema: "members",
                table: "members",
                type: "members.member_goal",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardian_document_number",
                schema: "members",
                table: "members",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DocumentType>(
                name: "guardian_document_type",
                schema: "members",
                table: "members",
                type: "members.document_type",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardian_name",
                schema: "members",
                table: "members",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardian_phone",
                schema: "members",
                table: "members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardian_relationship",
                schema: "members",
                table: "members",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "referred_by_member_id",
                schema: "members",
                table: "members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<MemberSource>(
                name: "source",
                schema: "members",
                table: "members",
                type: "members.member_source",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "tags",
                schema: "members",
                table: "members",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "member_changes",
                schema: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    old_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_by_role = table.Column<MemberChangeRole>(type: "members.member_change_role", nullable: false),
                    source = table.Column<MemberChangeSource>(type: "members.member_change_source", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_changes_member_member_id",
                        column: x => x.member_id,
                        principalSchema: "members",
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "member_notes",
                schema: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_notes_members_member_id",
                        column: x => x.member_id,
                        principalSchema: "members",
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // SQL a mano (Scripts/V2_M1.sql): documento 'cc', teléfonos a E.164 y el contacto de
            // emergencia anterior a una nota interna. DEBE ir antes de borrar emergency_contact: si
            // esta migración se regenera, EF suele detectar un "renombrado" emergency_contact -> address;
            // hay que cambiarlo por AddColumn(address) + esta llamada + DropColumn(emergency_contact).
            migrationBuilder.SqlScript("M1");

            migrationBuilder.DropColumn(
                name: "emergency_contact",
                schema: "members",
                table: "members");

            migrationBuilder.CreateIndex(
                name: "ix_members_gym_id_document_type_document_number",
                schema: "members",
                table: "members",
                columns: new[] { "gym_id", "document_type", "document_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_members_referred_by_member_id",
                schema: "members",
                table: "members",
                column: "referred_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_members_tags",
                schema: "members",
                table: "members",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_member_changes_gym_id",
                schema: "members",
                table: "member_changes",
                column: "gym_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_changes_member_id_changed_at",
                schema: "members",
                table: "member_changes",
                columns: new[] { "member_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_member_notes_gym_id",
                schema: "members",
                table: "member_notes",
                column: "gym_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_notes_member_id_created_at",
                schema: "members",
                table: "member_notes",
                columns: new[] { "member_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_members_members_referred_by_member_id",
                schema: "members",
                table: "members",
                column: "referred_by_member_id",
                principalSchema: "members",
                principalTable: "members",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_members_members_referred_by_member_id",
                schema: "members",
                table: "members");

            migrationBuilder.DropTable(
                name: "member_changes",
                schema: "members");

            migrationBuilder.DropTable(
                name: "member_notes",
                schema: "members");

            migrationBuilder.DropIndex(
                name: "ix_members_gym_id_document_type_document_number",
                schema: "members",
                table: "members");

            migrationBuilder.DropIndex(
                name: "ix_members_referred_by_member_id",
                schema: "members",
                table: "members");

            migrationBuilder.DropIndex(
                name: "ix_members_tags",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "document_type",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "emergency_contact_name",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "emergency_contact_phone",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "emergency_contact_relationship",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "gender",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "goal",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "guardian_document_number",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "guardian_document_type",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "guardian_name",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "guardian_phone",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "guardian_relationship",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "referred_by_member_id",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "tags",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "address",
                schema: "members",
                table: "members");

            migrationBuilder.AddColumn<string>(
                name: "emergency_contact",
                schema: "members",
                table: "members",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

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
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
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
                .OldAnnotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .OldAnnotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .OldAnnotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .OldAnnotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .OldAnnotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .OldAnnotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .OldAnnotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,cardio");

            migrationBuilder.CreateIndex(
                name: "ix_members_gym_id_document_number",
                schema: "members",
                table: "members",
                columns: new[] { "gym_id", "document_number" },
                unique: true);
        }
    }
}
