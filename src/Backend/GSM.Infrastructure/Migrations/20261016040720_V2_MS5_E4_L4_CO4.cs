using System;
using GSM.Domain.Equipment;
using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_MS5_E4_L4_CO4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes,tenure_months,referrals,manual")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual,document")
                .Annotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .Annotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership,gym_closed,membership_frozen,pass_exhausted,member_inactive,already_checked_in")
                .Annotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .Annotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .Annotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .Annotation("Npgsql:Enum:classes.waitlist_entry_status", "waiting,offered,confirmed,expired,left")
                .Annotation("Npgsql:Enum:communications.announcement_category", "gym_news,promotions")
                .Annotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived,scheduled")
                .Annotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .Annotation("Npgsql:Enum:communications.whatsapp_template_kind", "expiring_soon,expired,app_invitation,birthday")
                .Annotation("Npgsql:Enum:equipment.equipment_issue_reason", "not_working,damaged_part,strange_noise,missing_part,dirty,unsafe,other")
                .Annotation("Npgsql:Enum:equipment.equipment_issue_reporter_role", "member,staff")
                .Annotation("Npgsql:Enum:equipment.equipment_issue_status", "open,in_progress,resolved,dismissed")
                .Annotation("Npgsql:Enum:equipment.equipment_status", "available,maintenance,out_of_service")
                .Annotation("Npgsql:Enum:equipment.equipment_type", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked,link_copied,owner_added,owner_promoted,owner_removed,owner_demoted")
                .Annotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .Annotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.consent_channel", "app,front_desk,import")
                .Annotation("Npgsql:Enum:members.consent_type", "data_processing,liability_waiver")
                .Annotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .Annotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .Annotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .Annotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .Annotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .Annotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.duration_unit", "days,months")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .Annotation("Npgsql:Enum:memberships.plan_kind", "time,pass")
                .Annotation("Npgsql:Enum:memberships.renewal_kind", "new,early,in_grace,after_grace")
                .Annotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .Annotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed,skipped")
                .Annotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .Annotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .Annotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .Annotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired,equipment_issue_reported,equipment_issue_resolved")
                .Annotation("Npgsql:Enum:notifications.push_platform", "android,web_member,web_panel")
                .Annotation("Npgsql:Enum:payments.cash_movement_type", "withdrawal,deposit")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .Annotation("Npgsql:Enum:payments.void_request_status", "pending,approved,rejected")
                .Annotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .Annotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .Annotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .Annotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .Annotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance,weight_distance")
                .Annotation("Npgsql:Enum:superadmin.impersonation_mode", "read,write")
                .OldAnnotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes,tenure_months,referrals")
                .OldAnnotation("Npgsql:Enum:attendance.attendance_method", "qr,manual,document")
                .OldAnnotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .OldAnnotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership,gym_closed,membership_frozen,pass_exhausted,member_inactive,already_checked_in")
                .OldAnnotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .OldAnnotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .OldAnnotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .OldAnnotation("Npgsql:Enum:classes.waitlist_entry_status", "waiting,offered,confirmed,expired,left")
                .OldAnnotation("Npgsql:Enum:communications.announcement_category", "gym_news,promotions")
                .OldAnnotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived")
                .OldAnnotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .OldAnnotation("Npgsql:Enum:communications.whatsapp_template_kind", "expiring_soon,expired,app_invitation,birthday")
                .OldAnnotation("Npgsql:Enum:equipment.equipment_status", "available,maintenance,out_of_service")
                .OldAnnotation("Npgsql:Enum:equipment.equipment_type", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .OldAnnotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .OldAnnotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked,link_copied,owner_added,owner_promoted,owner_removed,owner_demoted")
                .OldAnnotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .OldAnnotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.consent_channel", "app,front_desk,import")
                .OldAnnotation("Npgsql:Enum:members.consent_type", "data_processing,liability_waiver")
                .OldAnnotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .OldAnnotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .OldAnnotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .OldAnnotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .OldAnnotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .OldAnnotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.duration_unit", "days,months")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .OldAnnotation("Npgsql:Enum:memberships.renewal_kind", "new,early,in_grace,after_grace")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed,skipped")
                .OldAnnotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .OldAnnotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .OldAnnotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .OldAnnotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired")
                .OldAnnotation("Npgsql:Enum:notifications.push_platform", "android,web_member,web_panel")
                .OldAnnotation("Npgsql:Enum:payments.cash_movement_type", "withdrawal,deposit")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .OldAnnotation("Npgsql:Enum:payments.void_request_status", "pending,approved,rejected")
                .OldAnnotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .OldAnnotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .OldAnnotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .OldAnnotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance,weight_distance")
                .OldAnnotation("Npgsql:Enum:superadmin.impersonation_mode", "read,write");

            migrationBuilder.AddColumn<int>(
                name: "entries_total",
                schema: "memberships",
                table: "memberships",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "entries",
                schema: "memberships",
                table: "membership_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<PlanKind>(
                name: "kind",
                schema: "memberships",
                table: "membership_plans",
                type: "memberships.plan_kind",
                nullable: false,
                defaultValue: PlanKind.Time);

            migrationBuilder.AddColumn<string>(
                name: "grant_reason",
                schema: "achievements",
                table: "member_achievements",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "granted_by",
                schema: "achievements",
                table: "member_achievements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revoke_reason",
                schema: "achievements",
                table: "member_achievements",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revoked_at",
                schema: "achievements",
                table: "member_achievements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "revoked_by",
                schema: "achievements",
                table: "member_achievements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "members_can_report_equipment",
                schema: "gyms",
                table: "gym_feature_flags",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                schema: "communications",
                table: "announcements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scheduled_for",
                schema: "communications",
                table: "announcements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "equipment_issues",
                schema: "equipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_by_role = table.Column<IssueReporterRole>(type: "equipment.equipment_issue_reporter_role", nullable: false),
                    reason = table.Column<EquipmentIssueReason>(type: "equipment.equipment_issue_reason", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    photo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    status = table.Column<EquipmentIssueStatus>(type: "equipment.equipment_issue_status", nullable: false, defaultValue: EquipmentIssueStatus.Open),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    seen_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipment_issues", x => x.id);
                    table.CheckConstraint("ck_equipment_issues_other_description", "reason <> 'other' OR description IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_equipment_issues_equipment_items",
                        column: x => x.item_id,
                        principalSchema: "equipment",
                        principalTable: "equipment_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pass_usages",
                schema: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    used_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pass_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_pass_usages_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "memberships",
                        principalTable: "memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_membership_plans_pass_entries",
                schema: "memberships",
                table: "membership_plans",
                sql: "(kind = 'pass') = (entries IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_scheduled_for",
                schema: "communications",
                table: "announcements",
                column: "scheduled_for",
                filter: "scheduled_for IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_equipment_issues_gym_id_status_created_at",
                schema: "equipment",
                table: "equipment_issues",
                columns: new[] { "gym_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_equipment_issues_item_id_status",
                schema: "equipment",
                table: "equipment_issues",
                columns: new[] { "item_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_equipment_issues_item_reporter_unclosed",
                schema: "equipment",
                table: "equipment_issues",
                columns: new[] { "item_id", "reported_by_user_id" },
                unique: true,
                filter: "reported_by_role = 'member' AND status IN ('open', 'in_progress')");

            migrationBuilder.CreateIndex(
                name: "ix_equipment_issues_reported_by_user_id",
                schema: "equipment",
                table: "equipment_issues",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pass_usages_attendance_id",
                schema: "memberships",
                table: "pass_usages",
                column: "attendance_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pass_usages_membership_id",
                schema: "memberships",
                table: "pass_usages",
                column: "membership_id");

            migrationBuilder.SqlScript("MS5");
            migrationBuilder.SqlScript("E4");
            migrationBuilder.SqlScript("L4");
            migrationBuilder.SqlScript("CO4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equipment_issues",
                schema: "equipment");

            migrationBuilder.DropTable(
                name: "pass_usages",
                schema: "memberships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_membership_plans_pass_entries",
                schema: "memberships",
                table: "membership_plans");

            migrationBuilder.DropIndex(
                name: "ix_announcements_scheduled_for",
                schema: "communications",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "entries_total",
                schema: "memberships",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "entries",
                schema: "memberships",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "memberships",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "grant_reason",
                schema: "achievements",
                table: "member_achievements");

            migrationBuilder.DropColumn(
                name: "granted_by",
                schema: "achievements",
                table: "member_achievements");

            migrationBuilder.DropColumn(
                name: "revoke_reason",
                schema: "achievements",
                table: "member_achievements");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                schema: "achievements",
                table: "member_achievements");

            migrationBuilder.DropColumn(
                name: "revoked_by",
                schema: "achievements",
                table: "member_achievements");

            migrationBuilder.DropColumn(
                name: "members_can_report_equipment",
                schema: "gyms",
                table: "gym_feature_flags");

            migrationBuilder.DropColumn(
                name: "image_url",
                schema: "communications",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "scheduled_for",
                schema: "communications",
                table: "announcements");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes,tenure_months,referrals")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual,document")
                .Annotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .Annotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership,gym_closed,membership_frozen,pass_exhausted,member_inactive,already_checked_in")
                .Annotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .Annotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .Annotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .Annotation("Npgsql:Enum:classes.waitlist_entry_status", "waiting,offered,confirmed,expired,left")
                .Annotation("Npgsql:Enum:communications.announcement_category", "gym_news,promotions")
                .Annotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived")
                .Annotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .Annotation("Npgsql:Enum:communications.whatsapp_template_kind", "expiring_soon,expired,app_invitation,birthday")
                .Annotation("Npgsql:Enum:equipment.equipment_status", "available,maintenance,out_of_service")
                .Annotation("Npgsql:Enum:equipment.equipment_type", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked,link_copied,owner_added,owner_promoted,owner_removed,owner_demoted")
                .Annotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .Annotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.consent_channel", "app,front_desk,import")
                .Annotation("Npgsql:Enum:members.consent_type", "data_processing,liability_waiver")
                .Annotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .Annotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .Annotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .Annotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .Annotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .Annotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.duration_unit", "days,months")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .Annotation("Npgsql:Enum:memberships.renewal_kind", "new,early,in_grace,after_grace")
                .Annotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .Annotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed,skipped")
                .Annotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .Annotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .Annotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .Annotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired")
                .Annotation("Npgsql:Enum:notifications.push_platform", "android,web_member,web_panel")
                .Annotation("Npgsql:Enum:payments.cash_movement_type", "withdrawal,deposit")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .Annotation("Npgsql:Enum:payments.void_request_status", "pending,approved,rejected")
                .Annotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .Annotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .Annotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .Annotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .Annotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance,weight_distance")
                .Annotation("Npgsql:Enum:superadmin.impersonation_mode", "read,write")
                .OldAnnotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes,tenure_months,referrals,manual")
                .OldAnnotation("Npgsql:Enum:attendance.attendance_method", "qr,manual,document")
                .OldAnnotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .OldAnnotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership,gym_closed,membership_frozen,pass_exhausted,member_inactive,already_checked_in")
                .OldAnnotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .OldAnnotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .OldAnnotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .OldAnnotation("Npgsql:Enum:classes.waitlist_entry_status", "waiting,offered,confirmed,expired,left")
                .OldAnnotation("Npgsql:Enum:communications.announcement_category", "gym_news,promotions")
                .OldAnnotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived,scheduled")
                .OldAnnotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .OldAnnotation("Npgsql:Enum:communications.whatsapp_template_kind", "expiring_soon,expired,app_invitation,birthday")
                .OldAnnotation("Npgsql:Enum:equipment.equipment_issue_reason", "not_working,damaged_part,strange_noise,missing_part,dirty,unsafe,other")
                .OldAnnotation("Npgsql:Enum:equipment.equipment_issue_reporter_role", "member,staff")
                .OldAnnotation("Npgsql:Enum:equipment.equipment_issue_status", "open,in_progress,resolved,dismissed")
                .OldAnnotation("Npgsql:Enum:equipment.equipment_status", "available,maintenance,out_of_service")
                .OldAnnotation("Npgsql:Enum:equipment.equipment_type", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .OldAnnotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .OldAnnotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked,link_copied,owner_added,owner_promoted,owner_removed,owner_demoted")
                .OldAnnotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .OldAnnotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.consent_channel", "app,front_desk,import")
                .OldAnnotation("Npgsql:Enum:members.consent_type", "data_processing,liability_waiver")
                .OldAnnotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .OldAnnotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .OldAnnotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .OldAnnotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .OldAnnotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .OldAnnotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.duration_unit", "days,months")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .OldAnnotation("Npgsql:Enum:memberships.plan_kind", "time,pass")
                .OldAnnotation("Npgsql:Enum:memberships.renewal_kind", "new,early,in_grace,after_grace")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed,skipped")
                .OldAnnotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .OldAnnotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .OldAnnotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .OldAnnotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired,equipment_issue_reported,equipment_issue_resolved")
                .OldAnnotation("Npgsql:Enum:notifications.push_platform", "android,web_member,web_panel")
                .OldAnnotation("Npgsql:Enum:payments.cash_movement_type", "withdrawal,deposit")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .OldAnnotation("Npgsql:Enum:payments.void_request_status", "pending,approved,rejected")
                .OldAnnotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .OldAnnotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .OldAnnotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .OldAnnotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance,weight_distance")
                .OldAnnotation("Npgsql:Enum:superadmin.impersonation_mode", "read,write");
        }
    }
}
