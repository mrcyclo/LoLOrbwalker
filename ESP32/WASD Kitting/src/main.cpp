#include <NimBLEDevice.h>
#include <Adafruit_NeoPixel.h>
#include <Preferences.h>
#include <USB.h>
#include <USBHIDKeyboard.h>
#include <USBHIDMouse.h>
#include <USBHIDConsumerControl.h>

Adafruit_NeoPixel pixel(1, 48, NEO_GRB + NEO_KHZ800);

USBCDC USBSerial;
USBHIDKeyboard keyboard;
USBHIDMouse mouse;
USBHIDConsumerControl consumer;

Preferences preferences;
const char *preferencesName = "keyboard-data";
const char *keyboardAddressKey = "address";

const NimBLEUUID serviceUuid("1812");
const uint32_t scanTimeMs = 5 * 1000;
const uint32_t reconnectTimeMs = 15 * 1000;
bool isConnected = false;
bool shouldReconnect = true;

bool isCheatActive = false;

// Lưu lại trạng thái vật lý thực tế của bàn phím mà không bị sửa đổi
KeyReport lastRawReport = {0};

void sendFilteredReport()
{
    KeyReport report;
    memcpy(&report, &lastRawReport, sizeof(KeyReport));

    // Nếu cheat mode đang bật, lọc bỏ các phím W (0x1A), A (0x04), S (0x16), D (0x07)
    if (isCheatActive)
    {
        for (int i = 0; i < 6; i++)
        {
            uint8_t k = report.keys[i];
            if (k == 0x1A || k == 0x04 || k == 0x16 || k == 0x07)
            {
                report.keys[i] = 0; // Xóa phím này khỏi báo cáo (máy tính sẽ hiểu là đã nhả phím)
            }
        }
    }

    keyboard.sendReport(&report);
}

void toggle_led(int r = 255, int g = 255, int b = 255)
{
    pixel.setPixelColor(0, pixel.Color(r, g, b));
    pixel.show();
    delay(100);
    pixel.setPixelColor(0, pixel.Color(0, 0, 0));
    pixel.show();
}

void notifyCallback(NimBLERemoteCharacteristic *pRemoteCharacteristic, uint8_t *pData, size_t length, bool isNotify)
{
    Serial.printf("Forwarding Raw HID: Handle %d, Length %d, Data: ", pRemoteCharacteristic->getHandle(), length);
    for (size_t i = 0; i < length; i++)
    {
        Serial.printf("%02X ", pData[i]);
    }
    Serial.println();

    if (length == sizeof(KeyReport))
    {
        memcpy(&lastRawReport, pData, sizeof(KeyReport));
        sendFilteredReport();
        return;
    }

    if (length == sizeof(uint16_t))
    {
        uint16_t mediaKey = pData[0] | (pData[1] << 8);
        if (mediaKey != 0)
        {
            consumer.press(mediaKey);
        }
        else
        {
            consumer.release();
        }
    }
}

class ClientCallbacks : public NimBLEClientCallbacks
{
    void onDisconnect(NimBLEClient *pClient, int reason) override
    {
        Serial.printf("Disconnected, reason = %d\n", reason);
        shouldReconnect = true;
        isConnected = false;
    }
} clientCallbacks;

void connectKeyboard()
{
    NimBLEScan *pScan = NimBLEDevice::getScan();

    Serial.println("Scan BLE devices");
    NimBLEScanResults results = pScan->getResults(scanTimeMs);
    for (int i = 0; i < results.getCount(); i++)
    {
        const NimBLEAdvertisedDevice *device = results.getDevice(i);
        if (!device->isAdvertisingService(serviceUuid))
            continue;

        String deviceName = device->getName().c_str();
        std::string rawAddress = device->getAddress().toString();
        Serial.printf("Device found: %s | Address: %s\n", deviceName, rawAddress.c_str());

        NimBLEClient *pClient = NimBLEDevice::createClient();
        if (!pClient)
            continue;

        Serial.println("Try to connect");
        if (!pClient->connect(device))
        {
            Serial.println("Failed to connect");
            NimBLEDevice::deleteClient(pClient);
            continue;
        }

        Serial.println("Connected; create secure connection");

        if (!pClient->secureConnection())
        {
            Serial.println("Failed to start securing connection");
            NimBLEDevice::deleteClient(pClient);
            continue;
        }

        Serial.println("Secure connection established");

        NimBLERemoteService *pService = pClient->getService(serviceUuid);
        if (pService == nullptr)
        {
            Serial.println("Failed to get service");
            NimBLEDevice::deleteClient(pClient);
            continue;
        }

        pClient->setClientCallbacks(&clientCallbacks);
        isConnected = true;

        preferences.begin(preferencesName);
        String deviceAddress = String(rawAddress.c_str());
        preferences.putString(keyboardAddressKey, deviceAddress);
        preferences.end();

        // Thiết lập chế độ giao tiếp (Protocol Mode) - UUID 2A4E
        NimBLERemoteCharacteristic *pProtocolMode = pService->getCharacteristic("2A4E");
        if (pProtocolMode != nullptr && (pProtocolMode->canWrite() || pProtocolMode->canWriteNoResponse()))
        {
            uint8_t protocolMode = 1; // 1 = Report Protocol, 0 = Boot Protocol
            pProtocolMode->writeValue(&protocolMode, 1, false);
            Serial.println("Protocol Mode defined as Report Protocol (1)");
        }

        // Có nhiều Report Characteristic (2A4D) cho Keyboard, Mouse, Consumer Control...
        // Nên ta duyệt qua tất cả và subscribe hết các characteristic có UUID 2A4D
        const auto &pCharacteristics = pService->getCharacteristics(true);
        for (auto pChar : pCharacteristics)
        {
            if (pChar->getUUID() == NimBLEUUID("2A4D"))
            {
                if (pChar->canNotify())
                {
                    if (pChar->subscribe(true, notifyCallback))
                    {
                        Serial.printf("Subscribed to characteristic Handle %d!\n", pChar->getHandle());
                    }
                    else
                    {
                        Serial.printf("Failed to subscribe notify Handle %d.\n", pChar->getHandle());
                    }
                }
            }
        }
    }

    pScan->clearResults();
}

void reconnectKeyboard()
{
    Serial.println("Reconnecting");

    preferences.begin(preferencesName, true);
    String savedAddress = preferences.getString(keyboardAddressKey);
    preferences.end();

    if (savedAddress == "")
        return;

    NimBLEClient *pClient = NimBLEDevice::createClient();
    pClient->setConnectTimeout(reconnectTimeMs);
    if (!pClient)
        return;

    Serial.printf("Try to connect %s\n", savedAddress.c_str());
    NimBLEAddress deviceAddress(savedAddress.c_str(), BLE_ADDR_RANDOM);
    if (!pClient->connect(deviceAddress))
    {
        Serial.println("Failed to connect");
        NimBLEDevice::deleteClient(pClient);
        return;
    }

    Serial.println("Connected; create secure connection");

    if (!pClient->secureConnection())
    {
        Serial.println("Failed to start securing connection");
        NimBLEDevice::deleteClient(pClient);
        return;
    }

    Serial.println("Secure connection established");

    NimBLERemoteService *pService = pClient->getService(serviceUuid);
    if (pService == nullptr)
    {
        Serial.println("Failed to get service");
        NimBLEDevice::deleteClient(pClient);
        return;
    }

    pClient->setClientCallbacks(&clientCallbacks);
    isConnected = true;

    // Thiết lập chế độ giao tiếp (Protocol Mode) - UUID 2A4E
    NimBLERemoteCharacteristic *pProtocolMode = pService->getCharacteristic("2A4E");
    if (pProtocolMode != nullptr && (pProtocolMode->canWrite() || pProtocolMode->canWriteNoResponse()))
    {
        uint8_t protocolMode = 1; // 1 = Report Protocol, 0 = Boot Protocol
        pProtocolMode->writeValue(&protocolMode, 1, false);
        Serial.println("Protocol Mode defined as Report Protocol (1)");
    }

    // Có nhiều Report Characteristic (2A4D) cho Keyboard, Mouse, Consumer Control...
    // Nên ta duyệt qua tất cả và subscribe hết các characteristic có UUID 2A4D
    const auto &pCharacteristics = pService->getCharacteristics(true);
    for (auto pChar : pCharacteristics)
    {
        if (pChar->getUUID() == NimBLEUUID("2A4D"))
        {
            if (pChar->canNotify())
            {
                if (pChar->subscribe(true, notifyCallback))
                {
                    Serial.printf("Subscribed to characteristic Handle %d!\n", pChar->getHandle());
                }
                else
                {
                    Serial.printf("Failed to subscribe notify Handle %d.\n", pChar->getHandle());
                }
            }
        }
    }
}

String serialReadLine()
{
    String line = "";
    while (USBSerial.available() > 0)
    {
        char c = USBSerial.read();
        if (c == '\n')
            break;

        line += c;
    }
    return line;
}

void setup()
{
    Serial.begin(115200);
    USBSerial.begin(115200);

    pixel.begin();
    pixel.setBrightness(5);
    pixel.show();

    USB.begin();
    keyboard.begin();
    mouse.begin();
    consumer.begin();

    Serial.println("Init NimBLEDevice");
    NimBLEDevice::init("WASD Kitting");

    // Yêu cầu xác thực bảo mật và lưu kết nối (bonding) để thiết bị hoàn thành pairing
    NimBLEDevice::setSecurityAuth(true, true, true);
    NimBLEDevice::setSecurityIOCap(BLE_HS_IO_NO_INPUT_OUTPUT);
}

void loop()
{
    // Connect to keyboard
    if (!isConnected)
    {
        if (shouldReconnect)
        {
            pixel.setPixelColor(0, pixel.Color(255, 127, 0));
            pixel.show();
            reconnectKeyboard();
            shouldReconnect = false;
        }
        else
        {
            pixel.setPixelColor(0, pixel.Color(0, 0, 255));
            pixel.show();
            connectKeyboard();
        }
    }

    if (isConnected)
    {
        pixel.setPixelColor(0, pixel.Color(0, 255, 0));
        pixel.show();
    }

    String line = serialReadLine();

    if (line == "get-name")
    {
        USBSerial.println("WASD Kitting");
    }

    if (line == "on")
    {
        if (!isCheatActive)
        {
            isCheatActive = true;
            // Xoá WASD khỏi bàn phím máy tính đang bấm ngay lập tức
            sendFilteredReport();
        }
    }

    if (line == "off")
    {
        if (isCheatActive)
        {
            isCheatActive = false;
            // Phục hồi lại các phím WASD đang nhấn vật lý (nếu có)
            sendFilteredReport();
        }
    }

    if (line == "click")
    {
        mouse.press(MOUSE_LEFT);
        delay(50);
        mouse.release(MOUSE_LEFT);
    }

    delay(10);
}
