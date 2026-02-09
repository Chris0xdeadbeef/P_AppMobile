using ReadMe.Pages;

namespace ReadMe
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            MainPage = new NavigationPage(new Menu());

        }
    }
}
