using LoLOrbwalker;
using System.Net.Http.Json;
using System.Timers;
using WindowsInput;
using WindowsInput.Native;
using static Vanara.PInvoke.User32;

var isSpaceHeld = false;
var attackSpeed = 1d;
var sim = new InputSimulator();
var lastAttack = DateTime.Now;

var fetchAttackSpeedTask = Task.Run(async () =>
{
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true
    };

    var client = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromSeconds(1.5)
    };

    ActivePlayer? player = null;
    while (true)
    {
        try
        {
            player = await client.GetFromJsonAsync<ActivePlayer>("https://127.0.0.1:2999/liveclientdata/activeplayer");
            if (player != null)
            {
                if (player.championStats.attackSpeed != attackSpeed)
                {
                    Console.WriteLine("Attack Speed: " + player.championStats.attackSpeed);
                }

                attackSpeed = player.championStats.attackSpeed;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION: " + ex.Message);
        }

        await Task.Delay(1000);
    }
});

while (true)
{
    isSpaceHeld = (GetAsyncKeyState(VK.VK_SPACE) & 0x8000) != 0;
    if (!isSpaceHeld)
    {
        await Task.Delay(10);
        continue;
    }

    var attackSlow = 1000 / Math.Max(attackSpeed, 0.1);
    var minDelay = attackSlow * 0.95;
    var attackDiff = (DateTime.Now - lastAttack).TotalMilliseconds;
    if (attackDiff < minDelay)
    {
        await Task.Delay(5);
        continue;
    }

    lastAttack = DateTime.Now;
    sim.Keyboard.KeyPress(VirtualKeyCode.VK_X);
    sim.Keyboard.KeyPress(VirtualKeyCode.VK_A);

    var windup = (int)Math.Ceiling(Math.Max(150, attackSlow * 0.33));
    await Task.Delay(windup);
    sim.Mouse.RightButtonClick();

    var postDelay = (int)Math.Ceiling(Math.Max(50, attackSlow * 0.15));
    var remaining = (int)Math.Ceiling(Math.Max(50, attackSlow - windup + postDelay));
    await Task.Delay(remaining);
}
