import SwiftUI
import BambuDryCore

struct PrinterSetupView: View {
    @EnvironmentObject var model: AppModel

    @State private var name: String = ""
    @State private var serial: String = ""
    @State private var ip: String = ""
    @State private var accessCode: String = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Add your Bambu printer")
                .font(.headline)
            Text("On the printer touchscreen: Settings → WLAN. The Access Code is on the same screen (8 characters).")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Label {
                Text("Enable **Developer Mode** on the printer (Settings → General → Developer Mode). Without it the firmware silently ignores remote drying commands over LAN — humidity will display, but auto-dry won't actually start the heater.")
                    .font(.caption)
                    .fixedSize(horizontal: false, vertical: true)
            } icon: {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
            }

            VStack(alignment: .leading, spacing: 4) {
                LabeledField(label: "Name", text: $name, placeholder: "My Bambu Printer")
                LabeledField(label: "IP",   text: $ip,   placeholder: "192.168.x.x")
                LabeledField(label: "Serial", text: $serial, placeholder: "01S00A2B…")
                LabeledField(label: "Access Code", text: $accessCode, placeholder: "8 chars", isSecure: true)
            }

            HStack {
                Spacer()
                Button("Save & Connect") {
                    var c = AppConfig.empty
                    c.printerName = name.isEmpty ? "My Bambu Printer" : name
                    c.serial = serial.trimmingCharacters(in: .whitespacesAndNewlines)
                    c.lanIP  = ip.trimmingCharacters(in: .whitespacesAndNewlines)
                    model.saveAndConnect(c, accessCode: accessCode.trimmingCharacters(in: .whitespacesAndNewlines))
                }
                .disabled(serial.isEmpty || ip.isEmpty || accessCode.count < 4)
                .keyboardShortcut(.defaultAction)
            }
        }
        .frame(width: 320)
        .onAppear {
            name = model.config.printerName
            serial = model.config.serial
            ip = model.config.lanIP
        }
    }
}

private struct LabeledField: View {
    let label: String
    @Binding var text: String
    let placeholder: String
    var isSecure: Bool = false

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(label).font(.caption).frame(width: 90, alignment: .trailing)
            if isSecure {
                SecureField(placeholder, text: $text)
                    .textFieldStyle(.roundedBorder)
            } else {
                TextField(placeholder, text: $text)
                    .textFieldStyle(.roundedBorder)
            }
        }
    }
}
