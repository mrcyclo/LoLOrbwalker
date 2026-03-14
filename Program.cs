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
var lastMove = DateTime.Now;

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

    var attackCooldown = 1000 / Math.Max(attackSpeed, 0.1);
    var windup = (int)Math.Ceiling(Math.Max(150, attackCooldown * 0.33));

    var isAttackable = (DateTime.Now - lastAttack).TotalMilliseconds >= attackCooldown;
    if (isAttackable)
    {
        lastAttack = DateTime.Now;
        sim.Keyboard.KeyPress(VirtualKeyCode.VK_X);
        await Task.Delay(windup);
    }

    var isMovable = (DateTime.Now - lastMove).TotalMilliseconds >= 500;
    if (isMovable)
    {
        lastMove = DateTime.Now;
        sim.Mouse.RightButtonClick();
        await Task.Delay(windup);
    }

    await Task.Delay(10);
}
