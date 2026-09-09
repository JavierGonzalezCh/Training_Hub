using System;
using System.Collections.Generic;
using GSM.Domain.Routines;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa09_1_ExerciseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "routines");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,transfer,card,other")
                .Annotation("Npgsql:Enum:routines.equipment", "barbell,dumbbell,kettlebell,machine,cable,smith_machine,bench,rack,resistance_band,trx,medicine_ball,mat,box,jump_rope,bodyweight,cardio_machine")
                .Annotation("Npgsql:Enum:routines.muscle", "chest,back,shoulders,biceps,triceps,forearms,quadriceps,hamstrings,glutes,calves,abs,lower_back,neck,full_body,cardio")
                .OldAnnotation("Npgsql:Enum:achievements.condition_type", "visits,streak")
                .OldAnnotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
                .OldAnnotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .OldAnnotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:members.member_status", "active,inactive")
                .OldAnnotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused")
                .OldAnnotation("Npgsql:Enum:payments.payment_method", "cash,transfer,card,other");

            migrationBuilder.CreateTable(
                name: "exercises",
                schema: "routines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    primary_muscle = table.Column<Muscle>(type: "routines.muscle", nullable: false),
                    secondary_muscles = table.Column<List<Muscle>>(type: "routines.muscle[]", nullable: false),
                    equipment = table.Column<List<Equipment>>(type: "routines.equipment[]", nullable: false),
                    video_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    image_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercises", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exercises_gym_id_name",
                schema: "routines",
                table: "exercises",
                columns: new[] { "gym_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exercises_primary_muscle",
                schema: "routines",
                table: "exercises",
                column: "primary_muscle");

            SeedSystemExercises(migrationBuilder);
        }

        // Catálogo del sistema (~40 ejercicios, gym_id = null). Va en la migración —no en DevSeeder—
        // porque tiene que existir también en producción. Cubre los 15 grupos musculares.
        private static void SeedSystemExercises(MigrationBuilder migrationBuilder)
        {
            (string Name, string Primary, string Secondary, string Equipment)[] rows =
            {
                ("Press de banca plana con barra", "chest", "triceps,shoulders", "barbell,bench"),
                ("Press de banca inclinado con mancuernas", "chest", "shoulders,triceps", "dumbbell,bench"),
                ("Aperturas con mancuernas", "chest", "shoulders", "dumbbell,bench"),
                ("Fondos en paralelas", "chest", "triceps,shoulders", "bodyweight"),
                ("Cruce de poleas", "chest", "shoulders", "cable"),
                ("Flexiones de pecho", "chest", "triceps,shoulders,abs", "bodyweight"),
                ("Aperturas con banda elástica", "chest", "shoulders", "resistance_band"),
                ("Dominadas", "back", "biceps,forearms", "rack,bodyweight"),
                ("Remo con barra", "back", "biceps,lower_back", "barbell"),
                ("Remo con mancuerna a una mano", "back", "biceps", "dumbbell,bench"),
                ("Jalón al pecho en polea", "back", "biceps", "cable,machine"),
                ("Peso muerto convencional", "back", "glutes,hamstrings,lower_back", "barbell"),
                ("Remo en máquina", "back", "biceps", "machine"),
                ("Remo en TRX", "back", "biceps,abs", "trx"),
                ("Press militar con barra", "shoulders", "triceps", "barbell,rack"),
                ("Press de hombros con mancuernas", "shoulders", "triceps", "dumbbell,bench"),
                ("Elevaciones laterales", "shoulders", "", "dumbbell"),
                ("Elevaciones posteriores (pájaros)", "shoulders", "", "dumbbell"),
                ("Face pull en polea", "shoulders", "biceps", "cable"),
                ("Curl de bíceps con barra", "biceps", "forearms", "barbell"),
                ("Curl alterno con mancuernas", "biceps", "forearms", "dumbbell"),
                ("Curl martillo", "biceps", "forearms", "dumbbell"),
                ("Curl de bíceps en polea", "biceps", "forearms", "cable"),
                ("Extensión de tríceps en polea", "triceps", "", "cable"),
                ("Press francés con barra", "triceps", "", "barbell,bench"),
                ("Fondos entre bancos", "triceps", "shoulders", "bench,bodyweight"),
                ("Patada de tríceps con mancuerna", "triceps", "", "dumbbell"),
                ("Curl de muñeca con barra", "forearms", "", "barbell"),
                ("Paseo del granjero", "forearms", "full_body", "dumbbell"),
                ("Sentadilla con barra", "quadriceps", "glutes,hamstrings,lower_back", "barbell,rack"),
                ("Prensa de piernas", "quadriceps", "glutes,hamstrings", "machine"),
                ("Zancadas con mancuernas", "quadriceps", "glutes,hamstrings", "dumbbell"),
                ("Extensión de cuádriceps en máquina", "quadriceps", "", "machine"),
                ("Sentadilla búlgara", "quadriceps", "glutes,hamstrings", "dumbbell,bench"),
                ("Curl femoral tumbado en máquina", "hamstrings", "calves", "machine"),
                ("Peso muerto rumano", "hamstrings", "glutes,lower_back", "barbell"),
                ("Buenos días con barra", "hamstrings", "glutes,lower_back", "barbell"),
                ("Hip thrust con barra", "glutes", "hamstrings", "barbell,bench"),
                ("Patada de glúteo en polea", "glutes", "hamstrings", "cable"),
                ("Puente de glúteos", "glutes", "hamstrings", "mat,bodyweight"),
                ("Elevación de talones de pie", "calves", "", "smith_machine"),
                ("Elevación de talones sentado", "calves", "", "machine"),
                ("Salto a la cuerda", "calves", "cardio", "jump_rope"),
                ("Crunch abdominal", "abs", "", "mat,bodyweight"),
                ("Plancha", "abs", "lower_back,shoulders", "mat,bodyweight"),
                ("Elevación de piernas colgado", "abs", "forearms", "rack,bodyweight"),
                ("Lanzamiento de balón medicinal", "abs", "chest,shoulders", "medicine_ball"),
                ("Rueda abdominal", "abs", "lower_back,shoulders", "mat"),
                ("Hiperextensiones lumbares", "lower_back", "glutes,hamstrings", "bench"),
                ("Encogimientos de trapecio con mancuernas", "neck", "shoulders", "dumbbell"),
                ("Burpees", "full_body", "chest,quadriceps,abs", "bodyweight"),
                ("Swing con kettlebell", "full_body", "glutes,hamstrings,back", "kettlebell"),
                ("Peso muerto con kettlebell", "full_body", "glutes,back", "kettlebell"),
                ("Subida al cajón", "full_body", "quadriceps,glutes", "box"),
                ("Cinta de correr", "cardio", "", "cardio_machine"),
                ("Bicicleta estática", "cardio", "quadriceps", "cardio_machine"),
                ("Remo en máquina de cardio", "cardio", "back", "cardio_machine"),
                ("Escaladores (mountain climbers)", "cardio", "abs", "mat,bodyweight"),
            };

            var values = string.Join(
                ",\n",
                rows.Select((r, i) => string.Format(
                    "('{0:D}'::uuid, '{1}', '{2}'::routines.muscle, '{{{3}}}'::routines.muscle[], '{{{4}}}'::routines.equipment[], now(), now())",
                    SystemExerciseId(i), r.Name.Replace("'", "''"), r.Primary, r.Secondary, r.Equipment)));

            migrationBuilder.Sql(
                "INSERT INTO routines.exercises " +
                "(id, name, primary_muscle, secondary_muscles, equipment, created_at, updated_at) VALUES\n" +
                values + ";");
        }

        private static Guid SystemExerciseId(int index)
            => new($"9e000000-0000-7000-8000-{index:D12}");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercises",
                schema: "routines");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:achievements.condition_type", "visits,streak")
                .Annotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .Annotation("Npgsql:Enum:members.member_status", "active,inactive")
                .Annotation("Npgsql:Enum:memberships.membership_status", "active,expired,paused")
                .Annotation("Npgsql:Enum:payments.payment_method", "cash,transfer,card,other")
                .OldAnnotation("Npgsql:Enum:achievements.condition_type", "visits,streak")
                .OldAnnotation("Npgsql:Enum:attendance.attendance_method", "qr,manual")
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
