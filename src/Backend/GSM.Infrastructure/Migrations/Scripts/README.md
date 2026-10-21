# Scripts SQL de migración

Todo SQL escrito a mano de la v2 (copiar datos, convertir enums, rellenar columnas) vive aquí como
`V2_<id>.sql` y la migración lo ejecuta, para que no se pierda cuando el integrador regenera la
migración sobre el modelo unido (ver `docs/DESARROLLO.md` §3).

Los archivos se embeben como recurso (`Migrations/Scripts/*.sql`). Uso en una migración:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // ...operaciones de esquema generadas...
    migrationBuilder.SqlScript("MS1_estado");   // ejecuta Scripts/V2_MS1_estado.sql
}
```

Reglas: el script debe ser idempotente cuando se pueda; `ALTER TYPE ... ADD VALUE` no se revierte y va
aquí con comentario. `V2_X0_Example.sql` es solo el ejemplo que ejercita el test del helper.
