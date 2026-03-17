using System.IO.Ports;
using System.Net.Http.Json;
using System.Text;
using static Vanara.PInvoke.User32;

namespace LoLOrbwalker
{
    internal class WASD
    {
        private double attackSpeed = 1d;
        private bool isGameActive = false;
        private DateTime lastAttack = DateTime.Now;
        private SerialPort serial = new SerialPort("COM9")
        {
            BaudRate = 115200,
        };

        public WASD()
        {
            Task.Run(GetAttackSpeedTask);
            Task.Run(CheckGameActiveTask);
            Task.Run(Loop);

            serial.Open();
        }

        private async Task Loop()
        {
            while (true)
            {
                if (!serial.IsOpen)
                {
                    await Task.Delay(10);
                    continue;
                }

                var isSpaceDown = (GetAsyncKeyState(VK.VK_SPACE) & 0x8000) != 0;
                if (!isSpaceDown || !isGameActive)
                {
                    await Task.Delay(10);
                    continue;
                }

                var attackCooldown = 1000 / Math.Max(attackSpeed, 0.1);
                var canAttack = (DateTime.Now - lastAttack).TotalMilliseconds >= attackCooldown;
                if (!canAttack)
                {
                    await Task.Delay(10);
                    continue;
                }

                serial.WriteLine("1");
                serial.WriteLine("2");

                lastAttack = DateTime.Now;

                var windup = (int)Math.Ceiling(Math.Max(150, attackCooldown * 0.33));
                await Task.Delay(windup);

                serial.WriteLine("0");
            }
        }

        private async Task GetAttackSpeedTask()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(1.5)
            };

            while (true)
            {
                try
                {
                    var player = await client.GetFromJsonAsync<ActivePlayer>("https://127.0.0.1:2999/liveclientdata/activeplayer");
                    if (player != null)
                    {
                        attackSpeed = player.championStats.attackSpeed;
                    }

                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION: " + ex.Message);
                    await Task.Delay(5000);
                }
            }
        }

        private async Task CheckGameActiveTask()
        {
            var bufferLength = 256;
            var buffer = new StringBuilder(bufferLength);

            while (true)
            {
                buffer.Clear();
                GetWindowText(GetForegroundWindow(), buffer, bufferLength);
                isGameActive = buffer.ToString() == "League of Legends (TM) Client";

                await Task.Delay(1000);
            }
        }
    }
}
