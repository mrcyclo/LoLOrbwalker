using SharpHook;
using System.IO.Ports;
using System.Net.Http.Json;
using System.Text;
using static Vanara.PInvoke.LCID;
using static Vanara.PInvoke.User32;

namespace LoLOrbwalker
{
    internal class WASD
    {
        private double attackSpeed = 1d;
        private bool isGameActive = false;
        private DateTime lastAttack = DateTime.Now;
        private TaskPoolGlobalHook hook = new();
        private SerialPort? serial = null;
        private int leftMouseDownCount = 0;

        public WASD()
        {
            hook.MousePressed += (s, e) =>
            {
                if (e.Data.Button != SharpHook.Data.MouseButton.Button1) return;
                leftMouseDownCount += 1;
            };

            hook.MouseReleased += (s, e) =>
            {
                if (e.Data.Button != SharpHook.Data.MouseButton.Button1) return;
                leftMouseDownCount -= 1;
            };
        }

        private async Task Loop()
        {
            while (true)
            {
                Console.Write("\rSerial: " + serial?.PortName + " - Attack Speed: " + string.Format("{0:n2}", attackSpeed) + " - Left Mouse Down: " + leftMouseDownCount);

                if (serial == null || !serial.IsOpen)
                {
                    await Task.Delay(10);
                    continue;
                }

                var isCheatActive = leftMouseDownCount > 0;
                if (!isCheatActive || !isGameActive)
                {
                    await Task.Delay(10);
                    continue;
                }

                var attackCooldown = 1000 / Math.Max(attackSpeed, 0.1);
                var canAttack = (DateTime.Now - lastAttack).TotalMilliseconds >= attackCooldown + 75;
                if (!canAttack)
                {
                    await Task.Delay(10);
                    continue;
                }

                serial.WriteLine("on");
                serial.WriteLine("click");

                lastAttack = DateTime.Now;

                var windup = (int)Math.Ceiling(Math.Max(150, attackCooldown * 0.33));
                await Task.Delay(windup);

                serial.WriteLine("off");
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
                if (!isGameActive)
                {
                    await Task.Delay(500);
                    continue;
                }

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
                var currentActive = buffer.ToString() == "League of Legends (TM) Client";

                // Turn off cheat when change game focus
                if (currentActive != isGameActive)
                {
                    serial.WriteLine("off");
                    isGameActive = currentActive;
                }

                await Task.Delay(1000);
            }
        }

        public void Start()
        {
            Task.Run(GetAttackSpeedTask);
            Task.Run(CheckGameActiveTask);
            Task.Run(() => hook.Run());
            Task.Run(Loop);

            serial = new SerialPort("COM9");
            serial.WriteTimeout = 1000;
            serial.Open();
        }
    }
}
