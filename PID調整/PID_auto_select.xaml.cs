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
            // UI から初期ゲインと目標値を取得
            if (!double.TryParse(TargetTextBox.Text, out double target))
            {
                ShowMessage("エラー", "目標値の取得に失敗しました。数値を入力してください。");
                return;
            }
            if (!double.TryParse(PTextBox.Text, out double P) ||
                !double.TryParse(ITextBox.Text, out double I) ||
                !double.TryParse(DTextBox.Text, out double D))
            {
                ShowMessage("エラー", "PID ゲインの取得に失敗しました。数値を入力してください。");
                return;
            }

            // 調整パラメータ
            const int maxIterations = 10;
            double overshootTol = 0.1 * target;   // 10% オーバーシュート許容
            double steadyTol = 0.05 * target;     // 5% 定常誤差許容

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // ゼロリセット
                await SendAndWaitZeroAsync(0, 0, 0, 0);

                // 現在のゲインでテスト
                await SendSettingsAsync(target, P, I, D);
                var metrics = await RecordResponseMetricsAsync(target, steadyTol);

                // 終了判定：3秒間ホールドかつオーバーシュート許容内
                if (metrics.Stable && metrics.Overshoot <= overshootTol)
                {
                    break;
                }

                // ゲイン調整ロジック
                if (metrics.Overshoot > overshootTol)
                {
                    // オーバーシュート過大 → P を減少し、D を増加
                    P *= 0.9;
                    D *= 1.1;
                }
                if (!metrics.Stable)
                {
                    // 安定せず／定常誤差大 → P, I を増加
                    P *= 1.05;
                    I *= 1.1;
                }
            }

            // 最終ゲイン適用
            await SendAndWaitZeroAsync(0, 0, 0, 0);
            await SendSettingsAsync(target, P, I, D);

            // UI 反映
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                PTextBox.Text = P.ToString("F2");
                ITextBox.Text = I.ToString("F2");
                DTextBox.Text = D.ToString("F2");
            });

            ShowMessage("完了", $"最適ゲイン: P={P:F2}, I={I:F2}, D={D:F2}");
        }

        private async Task SendAndWaitZeroAsync(double target, double p, double i, double d)
        {
            await SendSettingsAsync(target, p, i, d);
            while (Math.Abs(currentSpeed) > 0.01)
            {
                await Task.Delay(50);
            }
        }

        private async Task SendSettingsAsync(double target, double p, double i, double d)
        {
            string msg = $"{target},{p},{i},{d}";
            serialPort.WriteLine(msg);
            await Task.Delay(100);
        }

        /// <summary>
        /// 目標値に対するレスポンスを計測し、
        /// - ConvergenceTime: 全体の経過秒数
        /// - Overshoot: 最大オーバーシュート量
        /// - Stable: 誤差許容範囲内に入り、3秒間連続ホールドできたか
        /// を返す
        /// タイムアウトは5秒
        /// </summary>
        private async Task<(double ConvergenceTime, double Overshoot, bool Stable)>
            RecordResponseMetricsAsync(double target, double steadyTol)
        {
            var overallStart = DateTime.Now;
            DateTime withinStart = DateTime.MinValue;
            double maxOvershoot = 0;

            while ((DateTime.Now - overallStart).TotalSeconds <= 5)
            {
                double err = currentSpeed - target;
                // オーバーシュート計測
                maxOvershoot = Math.Max(maxOvershoot, Math.Max(0, currentSpeed - target));

                if (Math.Abs(err) <= steadyTol)
                {
                    // 許容内に入った瞬間を記録
                    if (withinStart == DateTime.MinValue)
                        withinStart = DateTime.Now;

                    // 3秒間ホールドを確認
                    if ((DateTime.Now - withinStart).TotalSeconds >= 3)
                    {
                        double convTime = (DateTime.Now - overallStart).TotalSeconds;
                        return (convTime, maxOvershoot, true);
                    }
                }
                else
                {
                    // 範囲外ならホールドタイマーをリセット
                    withinStart = DateTime.MinValue;
                }

                await Task.Delay(20);
            }

            double totalTime = (DateTime.Now - overallStart).TotalSeconds;
            return (totalTime, maxOvershoot, false);
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
                    portList.Add(port);
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
                serialPort = new SerialPort(portName, baudRate) { NewLine = "\r\n" };
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
