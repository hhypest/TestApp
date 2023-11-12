// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Major Code Smell", "S2376:Write-only properties should not be used", Justification = "<Ожидание>", Scope = "member", Target = "~P:TestApp.ViewModels.Ask.AskViewModel.IsSubcribeMessage")]
[assembly: SuppressMessage("Major Code Smell", "S2376:Write-only properties should not be used", Justification = "<Ожидание>", Scope = "member", Target = "~P:TestApp.ViewModels.Ask.IAskViewModel.IsSubcribeMessage")]
[assembly: SuppressMessage("Major Code Smell", "S2376:Write-only properties should not be used", Justification = "<Ожидание>", Scope = "member", Target = "~P:TestApp.ViewModels.Resolve.IResolveViewModel.IsSubscribeMessage")]
[assembly: SuppressMessage("Major Code Smell", "S2376:Write-only properties should not be used", Justification = "<Ожидание>", Scope = "member", Target = "~P:TestApp.ViewModels.Resolve.ResolveViewModel.IsSubscribeMessage")]
[assembly: SuppressMessage("Major Code Smell", "S2376:Write-only properties should not be used", Justification = "<Ожидание>", Scope = "member", Target = "~P:TestApp.ViewModels.Test.ITestViewModel.IsSubscribeMessage")]
[assembly: SuppressMessage("Major Code Smell", "S2376:Write-only properties should not be used", Justification = "<Ожидание>", Scope = "member", Target = "~P:TestApp.ViewModels.Test.TestViewModel.IsSubscribeMessage")]
[assembly: SuppressMessage("Performance", "CA1822:Пометьте члены как статические", Justification = "<Ожидание>", Scope = "member", Target = "~M:TestApp.ViewModels.Launch.LaunchViewModel.CanExecuteAcceptCreateTest(System.String)~System.Boolean")]
[assembly: SuppressMessage("Performance", "CA1822:Пометьте члены как статические", Justification = "<Ожидание>", Scope = "member", Target = "~M:TestApp.ViewModels.Ask.AskViewModel.CanExecuteChangeAnswer(TestApp.ViewModels.Answer.IAnswerViewModel)~System.Boolean")]
[assembly: SuppressMessage("Performance", "CA1822:Пометьте члены как статические", Justification = "<Ожидание>", Scope = "member", Target = "~M:TestApp.ViewModels.Test.TestViewModel.OnCanExecuteChangeAsk(TestApp.ViewModels.Ask.IAskViewModel)~System.Boolean")]
[assembly: SuppressMessage("Performance", "CA1822:Пометьте члены как статические", Justification = "<Ожидание>", Scope = "member", Target = "~M:TestApp.ViewModels.Test.TestViewModel.OnCanExecuteSaveTest(System.String)~System.Boolean")]