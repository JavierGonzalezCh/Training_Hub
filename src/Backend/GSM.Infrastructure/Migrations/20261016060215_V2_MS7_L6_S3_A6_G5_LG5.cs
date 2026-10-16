using System;
using GSM.Domain.Attendance;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_MS7_L6_S3_A6_G5_LG5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes,tenure_months,referrals,manual")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual,document")
                .Annotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .Annotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership,gym_closed,membership_frozen,pass_exhausted,member_inactive,already_checked_in")
                .Annotation("Npgsql:Enum:attendance.location_check", "inside,outside,denied")
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
                .Annotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired,equipment_issue_reported,equipment_issue_resolved,document_check_in,reward_expiring,subscription_trial_ending,subscription_trial_ended,subscription_expiring,subscription_expires_today,subscription_overdue,subscription_disable_soon,subscription_disabled,subscription_suspended,subscription_reactivated,subscription_payment_registered,member_limit_near,member_limit_reached")
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
                .OldAnnotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired,equipment_issue_reported,equipment_issue_resolved,document_check_in,reward_expiring")
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

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                schema: "memberships",
                table: "memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_group_holder",
                schema: "memberships",
                table: "memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "seats",
                schema: "memberships",
                table: "membership_plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "show_ranking",
                schema: "gyms",
                table: "gym_feature_flags",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<LocationCheck>(
                name: "location_check",
                schema: "attendance",
                table: "attendances",
                type: "attendance.location_check",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gym_onboarding",
                schema: "gyms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    skipped_by = table.Column<Guid>(type: "uuid", nullable: true),
                    skipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    skip_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gym_onboarding", x => x.id);
                    table.ForeignKey(
                        name: "fk_gym_onboarding_gyms",
                        column: x => x.gym_id,
                        principalSchema: "gyms",
                        principalTable: "gyms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership_groups",
                schema: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holder_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_groups", x => x.id);
                    table.CheckConstraint("ck_membership_groups_seats", "seats >= 2");
                });

            migrationBuilder.CreateTable(
                name: "ranking_participants",
                schema: "achievements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    use_alias = table.Column<bool>(type: "boolean", nullable: false),
                    alias = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ranking_participants", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_group_id",
                schema: "memberships",
                table: "memberships",
                column: "group_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_memberships_group_holder",
                schema: "memberships",
                table: "memberships",
                sql: "NOT is_group_holder OR group_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_membership_plans_group_is_time",
                schema: "memberships",
                table: "membership_plans",
                sql: "seats = 1 OR kind = 'time'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_membership_plans_seats",
                schema: "memberships",
                table: "membership_plans",
                sql: "seats >= 1 AND seats <= 500");

            migrationBuilder.CreateIndex(
                name: "ix_gym_onboarding_gym_id",
                schema: "gyms",
                table: "gym_onboarding",
                column: "gym_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_membership_groups_holder_member_id",
                schema: "memberships",
                table: "membership_groups",
                column: "holder_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_groups_plan_id",
                schema: "memberships",
                table: "membership_groups",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_ranking_participants_gym_id",
                schema: "achievements",
                table: "ranking_participants",
                column: "gym_id");

            migrationBuilder.CreateIndex(
                name: "ix_ranking_participants_member_id",
                schema: "achievements",
                table: "ranking_participants",
                column: "member_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_memberships_membership_group_group_id",
                schema: "memberships",
                table: "memberships",
                column: "group_id",
                principalSchema: "memberships",
                principalTable: "membership_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.SqlScript("MS7");
            migrationBuilder.SqlScript("L6");
            migrationBuilder.SqlScript("G5");
            migrationBuilder.SqlScript("LG5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS logs.platform_alerts, logs.alert_state;");

            migrationBuilder.DropForeignKey(
                name: "fk_memberships_membership_group_group_id",
                schema: "memberships",
                table: "memberships");

            migrationBuilder.DropTable(
                name: "gym_onboarding",
                schema: "gyms");

            migrationBuilder.DropTable(
                name: "membership_groups",
                schema: "memberships");

            migrationBuilder.DropTable(
                name: "ranking_participants",
                schema: "achievements");

            migrationBuilder.DropIndex(
                name: "ix_memberships_group_id",
                schema: "memberships",
                table: "memberships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_memberships_group_holder",
                schema: "memberships",
                table: "memberships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_membership_plans_group_is_time",
                schema: "memberships",
                table: "membership_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_membership_plans_seats",
                schema: "memberships",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "memberships",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "is_group_holder",
                schema: "memberships",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "seats",
                schema: "memberships",
                table: "membership_plans");

            migrationBuilder.DropColumn(
                name: "show_ranking",
                schema: "gyms",
                table: "gym_feature_flags");

            migrationBuilder.DropColumn(
                name: "location_check",
                schema: "attendance",
                table: "attendances");

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
                .Annotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired,equipment_issue_reported,equipment_issue_resolved,document_check_in,reward_expiring")
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
                .OldAnnotation("Npgsql:Enum:attendance.location_check", "inside,outside,denied")
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
                .OldAnnotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected,void_requested,important_announcement,void_request_rejected,class_reminder,instructor_class_reminder,birthday,waitlist_offer,waitlist_offer_expired,equipment_issue_reported,equipment_issue_resolved,document_check_in,reward_expiring,subscription_trial_ending,subscription_trial_ended,subscription_expiring,subscription_expires_today,subscription_overdue,subscription_disable_soon,subscription_disabled,subscription_suspended,subscription_reactivated,subscription_payment_registered,member_limit_near,member_limit_reached")
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
