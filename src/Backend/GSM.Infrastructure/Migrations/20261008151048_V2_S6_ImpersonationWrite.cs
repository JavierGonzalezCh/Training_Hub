using System;
using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_S6_ImpersonationWrite : Migration
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
                .Annotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked")
                .Annotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .Annotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .Annotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .Annotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .Annotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .Annotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .Annotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .Annotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .Annotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed")
                .Annotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .Annotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .Annotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .Annotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .Annotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .Annotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .Annotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .Annotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .Annotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .Annotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance")
                .Annotation("Npgsql:Enum:superadmin.impersonation_mode", "read,write")
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
                .OldAnnotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked")
                .OldAnnotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .OldAnnotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .OldAnnotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .OldAnnotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .OldAnnotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .OldAnnotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .OldAnnotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed")
                .OldAnnotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .OldAnnotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .OldAnnotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .OldAnnotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .OldAnnotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .OldAnnotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .OldAnnotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .OldAnnotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance");

            migrationBuilder.AddColumn<ImpersonationMode>(
                name: "mode",
                schema: "superadmin",
                table: "impersonation_sessions",
                type: "superadmin.impersonation_mode",
                nullable: false,
                defaultValue: ImpersonationMode.Read);

            migrationBuilder.CreateTable(
                name: "impersonation_actions",
                schema: "superadmin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impersonation_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_impersonation_actions_impersonation_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "superadmin",
                        principalTable: "impersonation_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_impersonation_actions_session_id_occurred_at",
                schema: "superadmin",
                table: "impersonation_actions",
                columns: new[] { "session_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "impersonation_actions",
                schema: "superadmin");

            migrationBuilder.DropColumn(
                name: "mode",
                schema: "superadmin",
                table: "impersonation_sessions");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak,routine_sessions,classes")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
                .Annotation("Npgsql:Enum:attendance.checkin_mode", "rotating,static")
                .Annotation("Npgsql:Enum:attendance.checkin_rejection_reason", "membership_expired,no_membership")
                .Annotation("Npgsql:Enum:classes.class_booking_status", "booked,cancelled,attended,no_show")
                .Annotation("Npgsql:Enum:classes.class_session_status", "scheduled,cancelled")
                .Annotation("Npgsql:Enum:classes.day_of_week", "monday,tuesday,wednesday,thursday,friday,saturday,sunday")
                .Annotation("Npgsql:Enum:communications.announcement_status", "draft,published,archived")
                .Annotation("Npgsql:Enum:communications.reaction_kind", "like,celebrate,strong")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked")
                .Annotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .Annotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .Annotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .Annotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .Annotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .Annotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .Annotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .Annotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .Annotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed")
                .Annotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .Annotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .Annotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .Annotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .Annotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .Annotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .Annotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .Annotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .Annotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .Annotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance")
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
                .OldAnnotation("Npgsql:Enum:identity.staff_access_audit_action", "invited,profile_changed,permission_granted,permission_revoked")
                .OldAnnotation("Npgsql:Enum:identity.staff_permission", "view_member_health,trainer,manage_attendance,void_payments,view_finances")
                .OldAnnotation("Npgsql:Enum:identity.staff_profile", "reception,trainer,administrator")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.document_type", "cc,ti,ce,pa,ppt,nit,other")
                .OldAnnotation("Npgsql:Enum:members.gender", "female,male,other,undisclosed")
                .OldAnnotation("Npgsql:Enum:members.member_change_role", "member,staff,owner,superadmin,system")
                .OldAnnotation("Npgsql:Enum:members.member_change_source", "panel,app,import,system")
                .OldAnnotation("Npgsql:Enum:members.member_goal", "weight_loss,muscle_gain,fitness,health,sport,other")
                .OldAnnotation("Npgsql:Enum:members.member_source", "walk_in,referral,social_media,google,flyer,corporate,other")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused,cancelled")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_channel", "push,email")
                .OldAnnotation("Npgsql:Enum:notifications.delivery_status", "pending,sent,failed")
                .OldAnnotation("Npgsql:Enum:notifications.notification_category", "membership,classes,progress,news,promotions,security")
                .OldAnnotation("Npgsql:Enum:notifications.notification_priority", "normal,urgent")
                .OldAnnotation("Npgsql:Enum:notifications.notification_status", "pending,held,sent,failed,cancelled,skipped")
                .OldAnnotation("Npgsql:Enum:notifications.notification_type", "class_cancelled,membership_expiring,achievement_unlocked,announcement,checkin_rejected")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,card,nequi,daviplata,breb,bank_transfer,bank_deposit,other")
                .OldAnnotation("Npgsql:Enum:routines.difficulty", "beginner,intermediate,advanced")
                .OldAnnotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .OldAnnotation("Npgsql:Enum:routines.exercise_type", "strength,cardio,mobility,stretching,plyometrics")
                .OldAnnotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,traps,obliques,adductors,abductors")
                .OldAnnotation("Npgsql:Enum:routines.schedule_mode", "rotating,weekly")
                .OldAnnotation("Npgsql:Enum:routines.tracking_type", "reps_weight,reps,time,distance,time_distance")
                .OldAnnotation("Npgsql:Enum:superadmin.impersonation_mode", "read,write");
        }
    }
}
