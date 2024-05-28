using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views
{
    public partial class MailsView : UserControl
    {
        public MailsView(MailsViewModel mailsViewModel)
        {
            InitializeComponent();
            this.DataContext = mailsViewModel;
        }
    }
}
