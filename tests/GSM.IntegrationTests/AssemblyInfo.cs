using Xunit;

// Los tests comparten la base de datos gsm_test y hacen TRUNCATE en su preparación:
// deben correr en serie.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
