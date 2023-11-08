using System.Collections.Generic;

namespace TestApp.Extensions;

public readonly record struct EditAskItem(string TitleAsk, bool IsSingleAnswer, IList<EditAnswerItem> AnswerList);

public readonly record struct EditAnswerItem(string TitleAnswer, bool IsAnswered);