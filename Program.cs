using LoLOrbwalker;
using System.IO.Ports;
using System.Net.Http.Json;
using System.Text;
using WindowsInput;
using WindowsInput.Native;
using static Vanara.PInvoke.User32;

var isSpaceHeld = false;
var isGameActive = false;
var attackSpeed = 1d;
var sim = new InputSimulator();
var lastAttack = DateTime.Now;
var lastMove = DateTime.Now;

//var serialPort = new System.IO.Ports.SerialPort("COM9", 115200)
//{
//    NewLine = "\n"
//};

//try
//{
//    serialPort.Open();
//}
//catch (Exception ex)
//{
//    Console.WriteLine($"EXCEPTION: {ex.Message}");
//    Console.WriteLine("Press Enter to exit...");
//    Console.ReadLine();
//    Environment.Exit(0);
//}

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

            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION: " + ex.Message);
            await Task.Delay(5000);
        }
    }
});

var gameActiveCheckTask = Task.Run(async () =>
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
});

while (true)
{
    isSpaceHeld = (GetAsyncKeyState(VK.VK_SPACE) & 0x8000) != 0;
    if (!isSpaceHeld || !isGameActive)
    {
        await Task.Delay(10);
        continue;
    }

    var attackCooldown = 1000 / Math.Max(attackSpeed, 0.1);
    var moveCooldown = Math.Min(100, attackCooldown);

    var isAttackable = (DateTime.Now - lastAttack).TotalMilliseconds >= attackCooldown;
    if (isAttackable)
    {
        lastAttack = DateTime.Now;
        sim.Keyboard.KeyPress(VirtualKeyCode.VK_X);
        //serialPort.WriteLine("key:x");


        var windup = (int)Math.Ceiling(Math.Max(210, attackCooldown * 0.33));
        await Task.Delay(windup);

        moveCooldown -= windup;
    }

    var isMovable = (DateTime.Now - lastMove).TotalMilliseconds >= moveCooldown;
    if (isMovable)
    {
        lastMove = DateTime.Now;
        sim.Mouse.RightButtonClick();
        //serialPort.WriteLine("mouse:right");

        var postDelay = (int)Math.Ceiling(Math.Max(50, attackCooldown * 0.15));
        await Task.Delay(postDelay);
    }

    await Task.Delay(10);
}
