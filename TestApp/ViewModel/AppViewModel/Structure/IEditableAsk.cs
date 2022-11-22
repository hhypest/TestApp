namespace TestApp.ViewModel.AppViewModel.Structure
{
    public interface IEditableAsk
    {
        public (string Option, bool IsAnswered)[] Answers { get; init; }

        public bool IsSingleAnswers { get; init; }

        public string Question { get; init; }
    }
}