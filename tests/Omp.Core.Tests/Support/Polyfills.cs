#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>Lets the compiler emit <c>init</c> accessors on .NET Framework.</summary>
    internal static class IsExternalInit
    {
    }

    /// <summary>Lets the compiler emit <c>required</c> members on .NET Framework.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }
    }
}
#endif
