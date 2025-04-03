using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PID調整
{
    public sealed partial class PID_graph : Page
    {
        public PID_graph()
        {
            this.InitializeComponent();
        }

        // シリアルポート一覧を更新するメソッド
        private async void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            // 更新開始: スピナーを表示
            progressRing.Visibility = Visibility.Visible;
            progressRing.IsActive = true;

            await LoadSerialPortsAsync();

            // 更新完了: スピナーを非表示
            progressRing.IsActive = false;
            progressRing.Visibility = Visibility.Collapsed;
        }

        // シリアルポートをComboBoxに表示するメソッド
        private async Task LoadSerialPortsAsync()
        {
            List<string> portList = new List<string>();
            string[] ports = SerialPort.GetPortNames();

            await Task.Run(() =>
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (var device in searcher.Get())
                    {
                        string name = device["Name"]?.ToString() ?? "Unknown Device";
                        int startIndex = name.LastIndexOf("(COM");
                        if (startIndex != -1)
                        {
                            int endIndex = name.IndexOf(")", startIndex);
                            if (endIndex != -1)
                            {
                                string portName = name.Substring(startIndex + 1, endIndex - startIndex - 1);
                                portList.Add($"{portName} - {name}");
                            }
                        }
                    }
                }
            });

            comboBoxPorts.ItemsSource = portList;

            if (portList.Count > 0)
            {
                comboBoxPorts.SelectedIndex = 0;
            }
        }

        // 選択されたポート番号を取得するメソッド
        private string GetSelectedPort()
        {
            if (comboBoxPorts.SelectedItem != null)
            {
                string selectedPort = comboBoxPorts.SelectedItem.ToString();
                return Regex.Match(selectedPort, @"COM\d+").Value;
            }
            return string.Empty;
        }

        // ComboBox で選択した数字の数に応じて ToggleSwitch を作成
        private void NumberComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NumberComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                int selectedNumber = int.Parse(selectedItem.Content.ToString());

                // 既存のスイッチをクリア
                SwitchContainer.Children.Clear();

                // 選択した数の ToggleSwitch を生成
                for (int i = 0; i < selectedNumber; i++)
                {
                    ToggleSwitch toggleSwitch = new ToggleSwitch
                    {
                        Header = $"ID {i + 1}",
                        HorizontalAlignment = HorizontalAlignment.Right, // 左寄せ
                        MinWidth = 65 // 必要なら設定（調整可）
                    };

                    // 初期状態（OFF）
                    toggleSwitch.OnContent = "表示";
                    toggleSwitch.OffContent = "非表示";

                    // Toggled イベントの追加
                    toggleSwitch.Toggled += ToggleSwitch_Toggled;

                    // ToggleSwitch を SwitchContainer に追加
                    SwitchContainer.Children.Add(toggleSwitch);
                }
            }
        }

        // ToggleSwitch の Toggled イベント
        private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            var toggleSwitch = sender as ToggleSwitch;

            if (toggleSwitch != null)
            {
                // ONの場合、"Hide" を表示、OFFの場合、"Show" を表示
                if (toggleSwitch.IsOn)
                {
                    toggleSwitch.OnContent = "表示";
                }
                else
                {
                    toggleSwitch.OffContent = "非表示";
                }
            }
        }
    }
}
