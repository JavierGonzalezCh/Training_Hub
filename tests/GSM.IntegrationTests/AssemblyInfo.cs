using Xunit;

// Los tests comparten la base de datos de tests (GSM_TEST_DATABASE) y hacen TRUNCATE en su preparación:
// deben correr en serie.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
