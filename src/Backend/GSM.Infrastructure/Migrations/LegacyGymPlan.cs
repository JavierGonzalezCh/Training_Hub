// El enum gym_plan que tenían gyms.gyms.plan y superadmin.gym_subscriptions.plan hasta Q7. Las migraciones
// históricas (y sus Designer y el snapshot hasta que se regenere) lo nombran; ya no es parte del modelo.
// Conserva el nombre GymPlan para que ese código compile sin tocarse. No usar en código nuevo.
namespace GSM.Infrastructure.Migrations;

internal enum GymPlan
{
    Basic,
    Pro,
    Enterprise,
}
