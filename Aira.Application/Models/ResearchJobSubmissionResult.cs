namespace Aira.Application.Models;

public sealed record ResearchJobSubmissionResult(
    bool IsAccepted,
    ResearchJobAcceptedResponse? AcceptedResponse,
    ProblemDescriptor? Problem)
{
    public static ResearchJobSubmissionResult Accepted(ResearchJobAcceptedResponse response) =>
        new(true, response, null);

    public static ResearchJobSubmissionResult Rejected(ProblemDescriptor problem) =>
        new(false, null, problem);
}
