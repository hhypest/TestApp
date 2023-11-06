using MaterialDesignThemes.Wpf;
using System.Windows;
using TestApp.Views.Dialog.DialogContent;

namespace TestApp.Views.Dialog;

public partial class DialogView : Window, IDialogView
{
    #region Константы
    private const PackIconKind _error = PackIconKind.ErrorOutline;
    private const PackIconKind _question = PackIconKind.QuestionBoxOutline;
    private const PackIconKind _warning = PackIconKind.WarningOutline;
    #endregion

    #region Конструктор
    public DialogView()
    {
        InitializeComponent();
    }
    #endregion

    #region Реализация интерфейса
    public void CreateView(string title, string message, TypeDialogView typeDialog)
    {
        CaptionView.Text = title;
        MessageView.Text = message;

        switch (typeDialog)
        {
            case TypeDialogView.InformationDialog:
                IconView.Kind = _warning;
                ContentView.Content = new InformationDialog(this);
                break;
            case TypeDialogView.QuestionDialog:
                IconView.Kind = _question;
                ContentView.Content = new QuestionDialog(this);
                break;
            case TypeDialogView.ErrorDialog:
                IconView.Kind = _error;
                ContentView.Content = new InformationDialog(this);
                break;
        }
    }

    public void ShowView()
    {
        ShowDialog();
    }
    #endregion
}