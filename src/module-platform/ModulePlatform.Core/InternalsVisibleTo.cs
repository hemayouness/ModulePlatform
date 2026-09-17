using System.Runtime.CompilerServices;

// Path normalisation is the platform's most security-sensitive helper, so it is
// unit-tested directly rather than only through the public package API.
[assembly: InternalsVisibleTo("ModulePlatform.Tests")]
