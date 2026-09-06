using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// A refusal as the API answers it (<c>docs/api.md</c>, Errors): the status and
/// the problem type, read together, because either alone is half the contract.
/// </summary>
internal static class Refusals
{
    public static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal($"/problems/{code}", problem.GetProperty("type").GetString());
            return problem;
        }
    }
}
