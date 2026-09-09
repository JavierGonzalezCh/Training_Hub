using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Routines;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ExerciseMapper
{
    public static partial ExerciseResponse ToResponse(Exercise exercise);
}
