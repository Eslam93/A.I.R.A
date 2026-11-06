namespace Proofsheet.Application.Abstractions;

public interface IReportChatAvailabilityService
{
    bool IsConfigured { get; }

    string UnavailableReason { get; }
}
