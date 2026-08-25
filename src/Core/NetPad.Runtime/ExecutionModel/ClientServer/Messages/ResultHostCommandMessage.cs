namespace NetPad.ExecutionModel.ClientServer.Messages;

public enum ResultHostCommand
{
    ClearResults = 1,
    HideEditor = 2,
    ShowEditor = 3,
    HideResults = 4,
    ShowResults = 5,
    AutoScrollResults = 6,
    OpenPanel = 7,
    RemovePanel = 8,
}

public record ResultHostCommandMessage(ResultHostCommand Command, string? PayloadJson);
