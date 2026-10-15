# RemoteGame：遠隔のお手本をなぞる

## 新しいシーンの流れ

`InputName → RemotePreview → RemoteGame`。RemotePreviewで筆跡をリアルタイム表示し、`experimentEnd`受信後に約1秒間完成を表示して自動遷移する。筆跡の座標をGlobalData.RemoteTemplateにコピーし、RemoteGameで読み込む。前段シーンが破棄されても筆跡は残り、RemoteGameは再受信しない。

現在はRemotePreviewの`Use Local Test Template`が有効。テスト文字「あいうえお」を1文字ずつ筆順に沿って少しずつ描き、完成後にRemoteGameへ進む。shodoVR受信に戻す際は**RemotePreviewManagerのこのチェック**を外す。前段から渡されるテスト／実受信の情報をRemoteGameが使うため、実受信時は通常どおりボードの準備を行い、テスト時だけ省略する。

shodoVR側も接続確認に対応した`codex/remote-trial-session`ブランチへ更新する。別PCへの取り込み・実機確認は別途必要。追加のスクリプトアタッチは不要で、既存のPenPosiとDrawingUdpSenderを使う。

## 接続確認・準備・再試行

- 筆跡はUDP 5005、接続確認はUDP 5006。両PCの設定を一致させ、LAN上でこの通信を許可する。shodoVRのReceiver IPは体験者PCのIPv4。
- 本番では、RemotePreviewとshodoVRの両側の「準備完了」を押してから描画できる。体験者側の準備は「お手本を観察する準備」を指す。バランスボードのキャリブレーションは従来どおりRemoteGameで行う。
- shodoVR側はペンタブと描画プレハブの準備を確認する。お題・参加者ID・試行IDも接続確認の通信で共有する。
- 最初の接続待ちは20秒、接続後の応答停止は5秒、お手本開始後の筆跡入力停止は60秒で中断する。筆跡入力の待ち時間はRemotePreviewManagerのDrawing Idle Timeout Secondsで変更可能（最小5秒）。
- エラー時は進行を止める。RemotePreviewの「やり直す」で筆跡を消し、新しい試行IDへ両側を切り替える。再び両側の準備完了が必要。RemoteGameの「最初からやり直す」でもお手本からやり直せる。
- 古い試行IDの筆跡は捨てる。完成合図は体験者側の受信確認まで0.5秒ごとに再送する。途中の点の再送は行わず、欠落が検出された試行はやり直す。
- テストモードは接続・準備ボタンを省略するが、試行記録は残す。本番のRemoteGame単体起動時は、接続確認を行うRemotePreviewへ進む。

## 試行記録

両PCの`Application.persistentDataPath/RemoteTrials/<試行ID>/`へ保存する。Windowsでは通常`%USERPROFILE%/AppData/LocalLow/<CompanyName>/<ProductName>/RemoteTrials/`。各プロジェクトのCompany/Productにより親フォルダは異なる。試行IDは共通で、ファイルは各PCに保存され、自動的なファイル集約は行わない。

- `learner_metadata.json` / `teacher_metadata.json`: 共通ID、役割、お題、匿名参加者ID、UTC開始時刻、テスト区分。参加者IDはRemotePreviewManagerのParticipant Idに入力する（任意）。
- `learner_events.jsonl`: お手本受信、準備、実際に描画された体験者の座標・線幅・補正後荷重、画の開始終了、消去、文字保存、全文字完了、中断理由。
- `teacher_events.jsonl`: 送信した筆跡座標・筆圧・文字／画番号・通し番号と、準備・進行状態。
- `template.json`: なぞりに使用する完成済みお手本の元座標。体験者側に保存する。

各イベントにUTC時刻とPC内の経過秒を付ける。両PCの時計を自動同期する機能はなく、厳密な時刻比較には時計を合わせる必要がある。お手本は送信側座標、体験者の筆跡はUnityワールド座標。`traceLayout`に紙の中心・大きさ・表示倍率を記録する。記録からの再生画面や自動採点は含まない。

やり直しでも前の記録を上書きせず、中断理由を残して新規フォルダを作る。ディスク保存に失敗した場合はエラーを表示して進行を止める。shodoVRの連携モードでは、この記録が従来Loggerの代わりとなる。

## 動作

RemoteGameを直接再生した場合は、Managerの`Use Local Test Template`で即時表示のテストに戻れる。InputNameから進む通常の流れではRemotePreviewの設定と受信データを使う。

1. InputNameの開始ボタン／EnterでRemotePreviewへ進む。
2. 接続後、両側の準備完了を押す。遠隔者がshodoVRのPenTabletでお手本を書く。RemotePreviewの中央に随時表示し、右側に文字ごとに保持する。
3. 遠隔者がEnterまたは追加の完成ボタンで通知する。完成した筆跡を引き継ぎ、自動でRemoteGameへ進む。
4. 実受信モードではバランスボードの接続・キャリブレーション後にカウントダウンを始める。テストモードではボードの準備を省略する。
5. 1文字目の筆跡を灰色で大きく表示する。バランスボードで移動し、WM-1／WM-3のBまたはSpaceで書く。
6. WM-2／WM-4のB、EnterまたはTabで保存し、次の文字を表示する。RemoteGameでは時間制限を無効にしている。
7. 全文字を保存したら、既存の完了表示と結果画面の選択へ進む。

テスト中は`Skip Balance Board In Local Test`も有効にしているため、バランスボードの接続・キャリブレーション・自動再接続を省略し、カウントダウンへ進む。このチェックを外すとローカルテストでもボードの準備を行う。`Use Local Test Template`を外した通常のUDP受信モードでは、必ず従来のボード準備を行う。ボード省略中のカーソル移動は追加していない。

## shodoVR側の設定と操作

対象コード：[DrawingUdpSender.cs](https://github.com/tamakikobayashi16/shodoVR/blob/HEAD/Assets/DrawingUdpSender.cs)、[PenPosi.cs](https://github.com/tamakikobayashi16/shodoVR/blob/HEAD/Assets/PenPosi.cs)。

- PenPosiの`udpSender`にDrawingUdpSenderを割り当てる。
- DrawingUdpSenderの`receiverIp`を体験者PCのIPv4アドレスにする。別PCの場合、`127.0.0.1`は使わない。
- `receiverPort`は`5005`。設定してから再生する。
- 入力したお題と同じ文字数のお手本を書く。1文字書くごとにSpaceで次の文字へ進む。書き直しはR。最後はEnterで単語全体の完成を送る。
- 1回の受信で使用する試行は1つ。再挑戦時は体験者側の「やり直す」を押す。

InputNameが空の場合は、受信した文字数を使う。文字そのものは通信データに含まれないため、結果画面にお題を表示したい場合はInputNameに入力する。

## 通信

お手本は最大5文字。6文字目の筆跡を受け取った場合はエラーを表示して遷移を止める。5文字目を書いた後のSpaceによる文字送りだけではエラーにしない。

## 表示倍率

書いている途中は固定倍率（初期値1.65倍）で表示する。Spaceの`nextCharacter`で直前の文字、Enterの`experimentEnd`で残りの文字を、全画の外接範囲から等比拡大・縮小して紙の中央に配置する。線幅を含めて紙の90%以内に収めるため、大きく書いた文字も収まる。ペンを離すだけの`strokeEnd`では調整しない。Rで消した文字は再び固定倍率で書き始める。テスト筆跡も文字ごとに同じ処理を行う。

RemotePreviewManagerの`Template Display Scale`、RemoteGameのManagerの`Remote Template Display Scale`で調整できる。完成後は1.65で紙の90%に収まり、それ以下では小さくなり、それ以上でもはみ出さないよう上限を設ける。両シーンを同じ値にすると同じ比率で表示する。元座標を引き継ぎ、RemoteGameでも同じ方法で文字ごとに調整する。5文字の右側一覧にも適用する。

shodoVRのJSON形式を受信する（既存HMDReceiverの50001番CSVとは別）。

```json
{"version":1,"session":"session-id","method":"PenTablet","eventType":"point","sequence":0,"character":0,"stroke":0,"x":0.1,"y":0.2,"z":10,"pressure":0.5}
```

`point`で追加、`clearCharacter`で該当文字だけ消去、`nextCharacter`では前の文字を保持、`experimentEnd`でお手本を固定する。`session`には接続確認で合意した試行IDを使う。`strokeEnd`の通し番号も処理して順序を管理する。順序の入れ替わりは150ms待って並べ直し、欠落が残ったらメッセージを表示してなぞりの開始を止める。

書いている途中の座標はPenPosiの標準範囲（X ±1.92、Y ±1.08）を紙へ等比変換する。完成後は実際の文字の範囲から自動調整するため、文字の大きさに合わせてSource Half Sizeを変える必要はない。途中の文字は範囲外にはみ出す場合がある。

両側の準備完了を条件に、体験者側から描画開始状態を通知する。描画開始後はshodoVRが筆跡を送信する。体験者のなぞり開始・全文字完了も遠隔側へ通知する。

## 検証

追加した接続・記録テストはSessionSmoke（17項目）。実際のDrawingUdpSenderとRemotePreviewをループバックで接続するSessionFlowSmokeでは、準備確認、筆跡停止タイムアウト、シーン再読み込みによるやり直し、両PC相当の共通ID記録、完成合図を1回落とした際の再送、次シーンへの引き継ぎを確認した。SessionFlowSmokeはshodoVRのDrawingUdpSender.csも隔離プロジェクトにコピーして実行する。shodoVR側の対応Unityは2022.3.62f2。

既存のUnity参照を使いプロジェクト内のC#をコンパイル済み。受信スクリプトを隔離したUnity 2021.3.4f1で、実UDP受信、文字の保持、書き直し、重複除外、順序の回復、座標変換、完成後の固定、ソケット再起動、欠落検出、データ引き継ぎ、テスト筆跡の逐次再生、文字完成時の中央配置・範囲調整の31項目を確認した。RemotePreviewのシーン読み込みからテスト筆跡の完成・RemoteGameへの自動遷移・5文字すべての筆跡の保持までを、隔離プロジェクトの簡易RemoteGameシーンで確認した。

テストコードは`Tests/RemoteHandwriting`、結果は`verification.txt`。再実行は必ず別のUnityプロジェクトで行う。受信スクリプト、RemoteTemplateData、RemoteTestHandwritingとテストをコピーし、テストをAssets/Editorに配置して`-executeMethod RemoteSmoke.Run`で起動する。標準のjsonserializeとphysicsモジュールが必要。シーン遷移テストのPreviewFlowSmokeは検証用RemoteGameを作成するため、TempRemoteVerificationという隔離プロジェクトでのみ実行できる。

実際の2台のPC、ペンタブ、バランスボード、Wiiリモコンを組み合わせた表示・操作確認は未実施。
