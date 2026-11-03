#!/usr/bin/env swift
// speech-stt — 本地音频转写 helper（Shoyee.Voice.Apple）。
//
// 姿势：独立 Swift 程序，stdout 出文本，.NET 侧用 Process 调用（同 Secretary 的 speech-stt）。
// 引擎：macOS Speech 框架（SFSpeechRecognizer）。系统自带、支持中文、不额外收费；
//       没有 on-device 模型时系统会走 Apple 的免费转写，音频不出本机。
//
// 用法：speech-stt <音频文件> [语言，缺省 zh-CN]
// 成功：stdout = 转写文本，exit 0
// 失败：stderr = 原因，exit 非 0（.NET 侧一律按 null 处理）
//
// ⚠️ 坑（Secretary 2026-09-30 实测）：识别回调投递在主 runloop——
//    在主线程上 semaphore.wait 死等会死锁到超时（0.7s 的活挂满 120s）。必须 dispatchMain()。
//
// 编译：swiftc -O speech-stt.swift -o speech-stt
import Foundation
import Speech

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(1)
}

func finish(_ code: Int32) -> Never {
    exit(code)
}

let args = CommandLine.arguments
guard args.count >= 2 else {
    fail("用法：speech-stt <音频文件> [语言]")
}

let filePath = args[1]
let language = args.count >= 3 ? args[2] : "zh-CN"

guard FileManager.default.fileExists(atPath: filePath) else {
    fail("文件不存在：\(filePath)")
}
let fileURL = URL(fileURLWithPath: filePath)

guard let recognizer = SFSpeechRecognizer(locale: Locale(identifier: language)) else {
    fail("不支持的语言：\(language)")
}
guard recognizer.isAvailable else {
    fail("语音识别服务不可用")
}

let request = SFSpeechURLRecognitionRequest(url: fileURL)
request.shouldReportPartialResults = false
request.requiresOnDeviceRecognition = false // 离线中文模型不保证存在，放行系统免费转写兜底

func startRecognition() {
    var finished = false
    _ = recognizer.recognitionTask(with: request) { result, error in
        guard !finished else { return }
        if let result, result.isFinal {
            finished = true
            FileHandle.standardOutput.write(Data(result.bestTranscription.formattedString.utf8))
            finish(0)
        }
        if let error {
            finished = true
            FileHandle.standardError.write(Data("识别失败：\(error.localizedDescription)\n".utf8))
            finish(2)
        }
    }

    // 兜底超时（.NET 侧也有超时，这里防 helper 自己挂着不退出）
    DispatchQueue.global().asyncAfter(deadline: .now() + 120) {
        if !finished {
            finished = true
            FileHandle.standardError.write(Data("识别超时（120s）\n".utf8))
            finish(3)
        }
    }

    dispatchMain()
}

// 授权：未决定 → 请求（首次弹系统授权框，归属 = 调用方 app）；拒绝 → 明确报错
switch SFSpeechRecognizer.authorizationStatus() {
case .authorized:
    startRecognition()
case .notDetermined:
    SFSpeechRecognizer.requestAuthorization { status in
        if status == .authorized {
            startRecognition()
        } else {
            fail("语音识别未授权（status=\(status.rawValue)），请在系统设置 → 隐私与安全性 → 语音识别中允许调用方")
        }
    }
    dispatchMain()
default:
    fail("语音识别未授权（status=\(SFSpeechRecognizer.authorizationStatus().rawValue)），请在系统设置 → 隐私与安全性 → 语音识别中允许调用方")
}
