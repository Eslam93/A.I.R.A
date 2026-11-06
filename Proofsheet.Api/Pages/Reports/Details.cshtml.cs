using Proofsheet.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Proofsheet.Api.Pages.Reports;

public sealed class DetailsModel : PageModel
{
    private readonly IReportChatAvailabilityService _reportChatAvailabilityService;

    public DetailsModel(IReportChatAvailabilityService reportChatAvailabilityService)
    {
        _reportChatAvailabilityService = reportChatAvailabilityService;
    }

    [FromRoute]
    public Guid JobId { get; set; }

    public bool IsReportChatConfigured => _reportChatAvailabilityService.IsConfigured;

    public string ReportChatUnavailableReason => _reportChatAvailabilityService.UnavailableReason;

    public void OnGet()
    {
    }
}
