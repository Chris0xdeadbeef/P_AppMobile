using ReadMe.Pages;

namespace ReadMe
{
    public partial class App : Application
    {
        public App(Menu menu)
        {
            InitializeComponent();

            MainPage = new SplashScreen(menu);
        }
    }
}
