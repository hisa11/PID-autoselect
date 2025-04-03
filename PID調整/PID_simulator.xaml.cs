using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PID調整
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PID_simulator : Page
    {
        public PID_simulator()
        {
            this.InitializeComponent();
        }
        private void NumberComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NumberComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                int selectedNumber = int.Parse(selectedItem.Content.ToString());

                // 既存のスイッチをクリア
                SwitchContainer.Children.Clear();

                // 選択した数のToggleSwitchを生成
                for (int i = 0; i < selectedNumber; i++)
                {
                    ToggleSwitch toggleSwitch = new ToggleSwitch
                    {
                        Header = $"Switch {i + 1}"
                    };
                    SwitchContainer.Children.Add(toggleSwitch);
                }
            }
        }
    }
}
