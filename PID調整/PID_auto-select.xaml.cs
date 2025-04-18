using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PID調整
{
    public sealed partial class PID_auto_select : Page
    {
        private SerialPort serialPort;
        private float currentSpeed = 0f;

        public PID_auto_select()
        {
            this.InitializeComponent();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // 自動チューニング開始
            if (serialPort == null || !serialPort.IsOpen)
            {
                ShowMessage("エラー", "シリアルポートが接続されていません。");
                return;
            }
            StartButton.IsEnabled = false;
            await AutoTuneAsync();
            StartButton.IsEnabled = true;
        }

        private async Task AutoTuneAsync()
        {
            // 候補ゲインのリスト (例)
            var gainCandidates = new[]
            {
                (P: 0.5, I: 0.1, D: 0.01),
                (P: 1.0, I: 0.2, D: 0.02),
                (P: 1.5, I: 0.3, D: 0.03)
            };

            double target = double.Parse(TargetTextBox.Text);
            double bestScore = double.MaxValue;
            (double P, double I, double D) bestGains = gainCandidates[0];

            foreach (var gains in gainCandidates)
            {
                // 1. ゼロリセット
                await SendAndWaitZeroAsync(0, 0, 0, 0);

                // 2. ゲインと目標値送信
                await SendSettingsAsync(target, gains.P, gains.I, gains.D);

                // 3. 応答記録
                var metrics = await RecordResponseMetricsAsync(target);

                // スコアリング (収束時間 + オーバーシュート係数)
                double score = metrics.ConvergenceTime + metrics.Overshoot;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestGains = gains;
                }

                // 次候補へ
                await Task.Delay(500);
            }

            // ベストゲイン適用
            await SendAndWaitZeroAsync(0, 0, 0, 0);
            await SendSettingsAsync(target, bestGains.P, bestGains.I, bestGains.D);

            // テキストボックスに反映
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                PTextBox.Text = bestGains.P.ToString("F2");
                ITextBox.Text = bestGains.I.ToString("F2");
                DTextBox.Text = bestGains.D.ToString("F2");
            });

            ShowMessage("完了", $"最適ゲイン: P={bestGains.P}, I={bestGains.I}, D={bestGains.D}");
        }

        // ゼロ状態になるまで待機
        private async Task SendAndWaitZeroAsync(double target, double p, double i, double d)
        {
            await SendSettingsAsync(target, p, i, d);
            // currentSpeed が十分小さくなるまで待つ
            while (Math.Abs(currentSpeed) > 0.01)
            {
                await Task.Delay(50);
            }
        }

        // ゲインと目標値送信
        private async Task SendSettingsAsync(double target, double p, double i, double d)
        {
            string msg = $"{target},{p},{i},{d}";
            serialPort.WriteLine(msg);
            // OK 応答待ち
            await Task.Delay(100);
        }

        // 応答を記録し、収束時間とオーバーシュートを計算
        private async Task<(double ConvergenceTime, double Overshoot)> RecordResponseMetricsAsync(double target)
        {
            var start = DateTime.Now;
            double maxOvershoot = 0;
            bool converged = false;
            while (true)
            {
                double err = currentSpeed - target;
                // オーバーシュート更新
                maxOvershoot = Math.Max(maxOvershoot, Math.Max(0, currentSpeed - target));

                // 収束判定: 誤差が5%以内で、以後安定
                if (!converged && Math.Abs(err) < 0.05 * target)
                {
                    converged = true;
                    break;
                }
                if ((DateTime.Now - start).TotalSeconds > 5)
                {
                    // タイムアウト
                    break;
                }
                await Task.Delay(20);
            }
            var convTime = (DateTime.Now - start).TotalSeconds;
            return (convTime, maxOvershoot);
        }

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
            var portList = new List<string>();
            await Task.Run(() =>
            {
                foreach (string port in SerialPort.GetPortNames())
                {
                    portList.Add(port);
                }
            });

            comboBoxPorts.ItemsSource = portList;
            if (portList.Count > 0)
                comboBoxPorts.SelectedIndex = 0;
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

        private void ConnectToSelectedPort()
        {
            string portName = comboBoxPorts.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(portName))
            {
                ShowMessage("エラー", "シリアルポートが選択されていません。");
                return;
            }

            if (!int.TryParse(((ComboBoxItem)PortsSpeed.SelectedItem)?.Content.ToString(), out int baudRate))
            {
                ShowMessage("エラー", "ボーレートの取得に失敗しました。");
                return;
            }

            try
            {
                serialPort = new SerialPort(portName, baudRate)
                {
                    NewLine = "\r\n"
                };
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();
                btnConnect.Content = "切断";
                ShowMessage("接続完了", $"{portName} に接続しました。");
            }
            catch (Exception ex)
            {
                ShowMessage("接続エラー", ex.Message);
            }
        }

        private void DisconnectSerialPort()
        {
            try
            {
                if (serialPort != null)
                {
                    serialPort.DataReceived -= SerialPort_DataReceived;
                    serialPort.Close();
                    serialPort.Dispose();
                    serialPort = null;
                }
                btnConnect.Content = "接続";
                ShowMessage("切断完了", "シリアルポートを切断しました。");
            }
            catch (Exception ex)
            {
                ShowMessage("切断エラー", ex.Message);
            }
        }

        // MCUからの速度受信
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = serialPort.ReadLine();
                if (float.TryParse(data, out float v))
                {
                    currentSpeed = v;
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        CurrentVelocityTextBlock.Text = $"Current Speed: {currentSpeed:F2}";
                    });
                }
            }
            catch { }
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
    }
}
