using SharpHook;
using SharpHook.Data;
using System.Net.Http.Json;
using System.Text;
using WindowsInput;
using WindowsInput.Native;
using static Vanara.PInvoke.User32;

namespace LoLOrbwalker
{
    internal class WASD
    {
        private double attackSpeed = 1d;
        private bool isGameActive = false;
        private bool isLeftMouseDown = false;
        private DateTime lastAttack = DateTime.Now;
        private InputSimulator sim = new();
        private TaskPoolGlobalHook hook = new();
        private HashSet<KeyCode> physicalKeysDown = new HashSet<KeyCode>();

        public WASD()
        {
            hook.KeyPressed += (s, e) => physicalKeysDown.Add(e.Data.KeyCode);
            hook.KeyReleased += (s, e) => physicalKeysDown.Remove(e.Data.KeyCode);

            hook.MousePressed += (s, e) =>
            {
                if (e.Data.Button != SharpHook.Data.MouseButton.Button1) return;
                isLeftMouseDown = true;
                Console.WriteLine("Active");
            };

            hook.MouseReleased += (s, e) =>
            {
                if (e.Data.Button != SharpHook.Data.MouseButton.Button1) return;
                isLeftMouseDown = false;
                Console.WriteLine("Deactive");
            };

            Task.Run(GetAttackSpeedTask);
            Task.Run(CheckGameActiveTask);
            Task.Run(Loop);
            Task.Run(() => hook.Run());
        }

        private List<VirtualKeyCode> GetActiveMoveKeys()
        {
            var list = new List<VirtualKeyCode>();
            if (physicalKeysDown.Contains(KeyCode.VcW)) list.Add(VirtualKeyCode.VK_W);
            if (physicalKeysDown.Contains(KeyCode.VcA)) list.Add(VirtualKeyCode.VK_A);
            if (physicalKeysDown.Contains(KeyCode.VcS)) list.Add(VirtualKeyCode.VK_S);
            if (physicalKeysDown.Contains(KeyCode.VcD)) list.Add(VirtualKeyCode.VK_D);
            return list;
        }

        private async Task Loop()
        {
            while (true)
            {
                if (!isLeftMouseDown || !isGameActive)
                {
                    await Task.Delay(10);
                    continue;
                }

                var attackCooldown = 1000 / Math.Max(attackSpeed, 0.1);
                var isAttackable = (DateTime.Now - lastAttack).TotalMilliseconds >= attackCooldown;
                if (!isAttackable)
                {
                    await Task.Delay(10);
                }

                lastAttack = DateTime.Now;

                var keysToRestore = GetActiveMoveKeys();
                foreach (var key in keysToRestore) sim.Keyboard.KeyUp(key);

                sim.Mouse.LeftButtonDown();
                await Task.Delay(50);
                sim.Mouse.LeftButtonUp();

                var windup = (int)Math.Ceiling(Math.Max(210, attackCooldown * 0.33));
                await Task.Delay(windup);

                foreach (var key in keysToRestore) sim.Keyboard.KeyDown(key);

                await Task.Delay(10);
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
