using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snowberry.IO.Common.Interfaces;

/// <summary>
/// Used for metadata about a binary model.
/// </summary>
public interface IBinaryModelMetadata
{
    /// <summary>
    /// The minimum supported version by the binary model.
    /// </summary>
    uint MinimumSupportedVersion { get; }

    /// <summary>
    /// The maximum supported version by the binary model.
    /// </summary>
    uint MaximumSupportedVersion { get; }

    /// <summary>
    /// The type size of the binary model.
    /// </summary>
    uint TypeSize { get; }

    /// <summary>
    /// The current and latest version of the binary model.
    /// </summary>
    uint CurrentVersion { get; }

    /// <summary>
    /// The actual version of the binary model instance.
    /// </summary>
    uint Version { get; set; }

    /// <summary>
    /// Determines whether the <see cref="TypeSize"/> is dynamic.
    /// </summary>
    /// <remarks>For instance, the <see cref="TypeSize"/> is considered dynamic if the binary model properties vary across different versions and lack a fixed size. Dynamic collections or strings are also examples of dynamic types.</remarks>
    bool IsTypeSizeDynamic { get; }
}
