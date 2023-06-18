using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Snowberry.IO.SourceGenerator;

internal partial class SerializeDeserializeGenerator
{
    public static readonly DiagnosticDescriptor TypeNotMarkedAsPartialDiagnostic
      = new("SIO001",
            "Type is not marked as partial",
            "The type '{0}' must be marked as partial",
            nameof(SerializeDeserializeGenerator),
            DiagnosticSeverity.Error,
            true);
}
