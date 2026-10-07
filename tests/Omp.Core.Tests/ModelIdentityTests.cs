using Newtonsoft.Json.Linq;
using Omp.Core.Session;

namespace Omp.Core.Tests;

public class ModelIdentityTests
{
    [Fact]
    public void TheVendorFamilyAndRevisionOmpReportsAreKept()
    {
        var view = ModelMapper.ToModelView(JObject.Parse("{\"id\":\"m\",\"provider\":\"p\",\"identity\":{\"class\":\"anthropic\",\"family\":\"opus\",\"revision\":\"5.5.0\"}}"));
        Assert.Equal(("anthropic", "opus", "5.5.0"), (view.VendorClass, view.Family, view.Revision));
        var none = ModelMapper.ToModelView(JObject.Parse("{\"id\":\"m\",\"provider\":\"p\"}"));
        Assert.Equal((null, null, null), (none.VendorClass, none.Family, none.Revision));
    }
}
