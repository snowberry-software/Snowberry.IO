using Snowberry.IO.Common.Interfaces;

namespace Snowberry.IO.SourceGenerator;

public partial class BinaryModelGenerator
{
    public const string c_CustomNamespace = "Snowberry.IO.Generator.Attributes";
    public const string c_BinarySerializationAttributeName = "BinarySerializationAttribute";
    public const string c_BinaryIgnoreAttributeName = "BinaryIgnoreAttribute";
    public const string c_BinaryPropertyAttributeName = "BinaryPropertyAttribute";

    public static class BinaryPropertyConstants
    {
        public const string c_MinimumVersionPropertyName = "MinimumVersion";
        public const string c_MaximumVersionPropertyName = "MaximumVersion";
        public const string c_EndianTypePropertyName = "EndianType";
        public const string c_IndexPropertyName = "Index";
    }

    public static class BinaryModelConstants
    {
        public const string c_CurrentVersionConstantName = $"c_{nameof(IBinaryModelMetadata.CurrentVersion)}";
        public const string c_MinimumSupportedVersionConstantName = $"c_{nameof(IBinaryModelMetadata.MinimumSupportedVersion)}";
        public const string c_MaximumSupportedVersionConstantName = $"c_{nameof(IBinaryModelMetadata.MaximumSupportedVersion)}";
        public const string c_IsTypeSizeDynamicConstantName = $"c_{nameof(IBinaryModelMetadata.IsTypeSizeDynamic)}";
    }
}
