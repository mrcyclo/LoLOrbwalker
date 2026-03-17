#include <NimBLEDevice.h>
#include <Adafruit_NeoPixel.h>
#include "USB.h"
#include "USBHIDKeyboard.h"
#include "USBHIDMouse.h"

#define LED_TX 43
#define LED_RX 44
Adafruit_NeoPixel pixel(1, 48, NEO_GRB + NEO_KHZ800);

USBHIDKeyboard keyboard;
USBHIDMouse mouse;

NimBLEUUID serviceUuid("1812");
bool connected = false;

bool isCheatActive = false;

// Lưu lại trạng thái vật lý thực tế của bàn phím mà không bị sửa đổi
KeyReport lastRawReport = {0};

void sendFilteredReport() {
    KeyReport report;
    memcpy(&report, &lastRawReport, sizeof(KeyReport));

    // Nếu cheat mode đang bật, lọc bỏ các phím W (0x1A), A (0x04), S (0x16), D (0x07)
    if (isCheatActive) {
        for (int i = 0; i < 6; i++) {
            uint8_t k = report.keys[i];
            if (k == 0x1A || k == 0x04 || k == 0x16 || k == 0x07) {
                report.keys[i] = 0; // Xóa phím này khỏi báo cáo (máy tính sẽ hiểu là đã nhả phím)
            }
        }
    }

    keyboard.sendReport(&report);

    digitalWrite(LED_TX, LOW);
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
    if (pRemoteCharacteristic->getHandle() != 41 || length != 8)
        return;

    memcpy(&lastRawReport, pData, sizeof(KeyReport));
    sendFilteredReport();
}

String serialReadLine() {
    String line = "";
    while (Serial.available() > 0) {
        char c = Serial.read();
        if (c == '\n')
            break;

        line += c;
    }
    return line;
}

void setup()
{
    Serial.begin(115200);

    pixel.begin();
    pixel.setBrightness(10);
    pixel.show();

    pinMode(LED_TX, OUTPUT);
    pinMode(LED_RX, OUTPUT);

    digitalWrite(LED_TX, HIGH);
    digitalWrite(LED_RX, HIGH);

    USB.begin();
    keyboard.begin();
    mouse.begin();

    Serial.println("Init NimBLEDevice");
    NimBLEDevice::init("WASD Kitting");

    // Yêu cầu xác thực bảo mật và lưu kết nối (bonding) để thiết bị hoàn thành pairing
    NimBLEDevice::setSecurityAuth(true, true, true);
    NimBLEDevice::setSecurityIOCap(BLE_HS_IO_NO_INPUT_OUTPUT);

    Serial.println("Scan BLE devices");
    NimBLEScan *pScan = NimBLEDevice::getScan();
    NimBLEScanResults results = pScan->getResults(10 * 1000);

    for (int i = 0; i < results.getCount(); i++)
    {
        const NimBLEAdvertisedDevice *device = results.getDevice(i);
        if (!device->isAdvertisingService(serviceUuid))
            continue;

        Serial.printf("Device found: %s | Address: %s\r\n", device->getName().c_str(), device->getAddress().toString().c_str());

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
            return;
        }

        Serial.println("Secure connection established");

        NimBLERemoteService *pService = pClient->getService(serviceUuid);
        if (pService != nullptr)
        {
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
            const auto& pCharacteristics = pService->getCharacteristics(true);
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
    }

    pScan->clearResults();
}

void loop()
{
    digitalWrite(LED_TX, HIGH);
    digitalWrite(LED_RX, HIGH);

    String line = serialReadLine();

    if (line == "1") {
        if (!isCheatActive) {
            isCheatActive = true;
            // Xoá WASD khỏi bàn phím máy tính đang bấm ngay lập tức
            sendFilteredReport();
        }
    }

    if (line == "0") {
        if (isCheatActive) {
            isCheatActive = false;
            // Phục hồi lại các phím WASD đang nhấn vật lý (nếu có)
            sendFilteredReport();
        }
    }

    if (line == "2") {
        mouse.press(MOUSE_LEFT);
        delay(50);
        mouse.release(MOUSE_LEFT);

        digitalWrite(LED_RX, LOW);
    }

    delay(10);
}
