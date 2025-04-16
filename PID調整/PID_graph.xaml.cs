using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Linq;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

namespace PID調整
{
    public sealed partial class PID_graph : Page
    {
        private SerialPort serialPort;
        private LineSeries<ObservablePoint> lineSeries;
        private ObservableCollection<ObservablePoint> chartValues = new ObservableCollection<ObservablePoint>();
        private double xValue = 0;

        public PID_graph()
        {
            this.InitializeComponent();

            // グラフ初期設定
            lineSeries = new LineSeries<ObservablePoint>
            {
                Values = chartValues,
                Fill = null // 塗りつぶしなし
            };

            myChart.Series = new ISeries[] { lineSeries };
            myChart.XAxes = new[] { new Axis { Name = "Time" } };
            myChart.YAxes = new[] { new Axis { Name = "Value" } };
        }

        // シリアルポート一覧を更新
        private async void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            progressRing.Visibility = Visibility.Visible;
            progressRing.IsActive = true;

            await LoadSerialPortsAsync();

            progressRing.IsActive = false;
            progressRing.Visibility = Visibility.Collapsed;
        }

        private async Task LoadSerialPortsAsync()
        {
            List<string> portList = new List<string>();

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

        private string GetSelectedPort()
        {
            if (comboBoxPorts.SelectedItem != null)
            {
                string selectedPort = comboBoxPorts.SelectedItem.ToString();
                return Regex.Match(selectedPort, @"COM\d+").Value;
            }
            return string.Empty;
        }

        private void ButtonConnect_Click(object sender, RoutedEventArgs e)
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                DisconnectSerialPort();
            }
            else
            {
                ConnectToSelectedPort();
            }
        }


        private int GetPortsSpeed()
        {
            if (PortsSpeed.SelectedItem is ComboBoxItem selectedItem)
            {
                if (int.TryParse(selectedItem.Content.ToString(), out int baudRate))
                {
                    return baudRate;
                }
            }

            // 無効な選択や未選択の場合はデフォルト値
            return 9600;
        }



        private void ConnectToSelectedPort()
        {
            string portName = GetSelectedPort();
            int selectedSpeed = GetPortsSpeed();

            if (string.IsNullOrEmpty(portName))
            {
                ShowMessage("エラー", "シリアルポートが選択されていません。");
                return;
            }

            try
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.Close();
                }

                serialPort = new SerialPort(portName, selectedSpeed);
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();

                btnConnect.Content = "切断"; // ← 接続成功後にボタン名変更
                ShowMessage("接続完了", $"{portName} に {selectedSpeed}bps で接続しました。");
            }
            catch (Exception ex)
            {
                ShowMessage("接続エラー", ex.Message);
            }
        }



        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string receivedData = serialPort.ReadLine();
                string[] tokens = receivedData.Trim().Split(',');

                int idCount = tokens.Length / 2;

                if (tokens.Length % 2 == 0 && idCount == targetSeriesList.Count)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        for (int i = 0; i < idCount; i++)
                        {
                            if (double.TryParse(tokens[i * 2], out double targetVal) &&
                                double.TryParse(tokens[i * 2 + 1], out double currentVal))
                            {
                                var x = xValue;

                                if (targetSeriesList[i].Values is ObservableCollection<ObservablePoint> targetValues)
                                {
                                    targetValues.Add(new ObservablePoint(x, targetVal));
                                    if (targetValues.Count > 50) targetValues.RemoveAt(0);
                                }

                                if (currentSeriesList[i].Values is ObservableCollection<ObservablePoint> currentValues)
                                {
                                    currentValues.Add(new ObservablePoint(x, currentVal));
                                    if (currentValues.Count > 50) currentValues.RemoveAt(0);
                                }
                            }
                        }

                        xValue += 1;
                    });
                }
            }
            catch
            {
                // 例外は無視かログ
            }
        }


        private async void ShowMessage(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void NumberComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NumberComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                int selectedNumber = int.Parse(selectedItem.Content.ToString());

                InitializeChartSeries(selectedNumber); // ← グラフ系列初期化を追加

                SwitchContainer.Children.Clear();
                for (int i = 0; i < selectedNumber; i++)
                {
                    ToggleSwitch toggleSwitch = new ToggleSwitch
                    {
                        Header = $"ID {i + 1}",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        MinWidth = 65,
                        OnContent = "表示",
                        OffContent = "非表示",
                        IsOn = true
                    };
                    toggleSwitch.Toggled += ToggleSwitch_Toggled;
                    SwitchContainer.Children.Add(toggleSwitch);
                }
            }
        }


        private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            var toggleSwitch = sender as ToggleSwitch;

            if (toggleSwitch != null)
            {
                toggleSwitch.OnContent = "表示";
                toggleSwitch.OffContent = "非表示";
            }
        }
        private void DisconnectSerialPort()
        {
            try
            {
                serialPort.Close();
                serialPort = null;
                btnConnect.Content = "接続"; // ← 切断後にボタン名戻す
                ShowMessage("切断完了", "シリアルポートを切断しました。");
            }
            catch (Exception ex)
            {
                ShowMessage("切断エラー", ex.Message);
            }
        }


        private List<LineSeries<ObservablePoint>> targetSeriesList = new(); // 点線（目標値）
        private List<LineSeries<ObservablePoint>> currentSeriesList = new(); // 実線（現在値）

        private static readonly SKColor[] seriesColors = new SKColor[]
        {
             SKColors.Red, SKColors.Blue, SKColors.Green, SKColors.Orange, SKColors.Purple,
                SKColors.Brown, SKColors.Turquoise, SKColors.Magenta
        };

        private void InitializeChartSeries(int numberOfIDs)
        {
            targetSeriesList.Clear();
            currentSeriesList.Clear();
            myChart.Series = new List<ISeries>();

            for (int i = 0; i < numberOfIDs; i++)
            {
                var color = seriesColors[i % seriesColors.Length];

                // Create a dashed line for the target series
                var targetStroke = new SolidColorPaint(color, 2)
                {
                    PathEffect = new DashEffect(new float[] { 6, 6 }) // Creating the dashed line effect
                };

                // Create a solid line for the current series
                var currentStroke = new SolidColorPaint(color, 2);

                var targetSeries = new LineSeries<ObservablePoint>
                {
                    Values = new ObservableCollection<ObservablePoint>(),
                    Stroke = targetStroke,
                    GeometryStroke = null,
                    GeometryFill = null,
                    LineSmoothness = 0,
                    Name = $"ID{i + 1}_目標"
                };

                var currentSeries = new LineSeries<ObservablePoint>
                {
                    Values = new ObservableCollection<ObservablePoint>(),
                    Stroke = currentStroke,
                    GeometryStroke = null,
                    GeometryFill = null,
                    LineSmoothness = 0,
                    Name = $"ID{i + 1}_現在"
                };

                targetSeriesList.Add(targetSeries);
                currentSeriesList.Add(currentSeries);

                myChart.Series = myChart.Series.Append(targetSeries).Append(currentSeries).ToArray();
            }

            xValue = 0; // x軸の初期化
        }



    }
}
