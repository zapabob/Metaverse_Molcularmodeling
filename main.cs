using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using VRC.SDKBase;
using System.Collections.Generic;
using System.IO;

public class VRMoleculeViewer : MonoBehaviour
{
    // PDB ファイルのパス
    public string pdbFilePath;

    // 原子と結合を表現するためのプレハブ
    public GameObject atomPrefab;
    public GameObject bondPrefab;

    // 原子種別と色を対応付ける辞書
    private Dictionary<string, Color> elementColors = new Dictionary<string, Color>
    {
        { "C", Color.gray },
        { "H", Color.white },
        { "O", Color.red },
        { "N", Color.blue },
        // ... その他の原子種別を追加 ...
    };

    // 分子構造データ
    private List<AtomData> atomDataList = new List<AtomData>();
    private List<BondData> bondDataList = new List<BondData>();

    // 分子モデルのルートオブジェクト
    private GameObject moleculeModelRoot;

    // VR コントローラーのイベントを受け取る
    private XRBaseInteractor interactor;

    // 選択中の原子
    private GameObject selectedAtom;

    // VRChat UI
    public VRCUiPage fileSelectionPage; // PDB ファイル選択 UI
    public VRCUiPage infoPage; // 原子情報表示 UI
    public VRCUiElement atomInfoText; // 原子情報表示テキスト
    public VRCUiPage animationSettingsPage; // アニメーション設定 UI
    public VRCUiSlider animationFrequencySlider; // 振動周波数設定
    public VRCUiSlider animationAmplitudeSlider; // 振動振幅設定
    public VRCUiPage materialSelectionPage; // 材質選択 UI
    public VRCUiElement[] materialElements; // 材質選択 UI の要素

    // モデルの保存/読み込み
    private string saveFilePath; // モデル保存用のファイルパス

    // アニメーション用パラメータ
    private float animationFrequency = 1.0f; // 振動周波数
    private float animationAmplitude = 0.1f; // 振動振幅

    // 材質ライブラリ
    public Material[] materials;
    // 選択中の材質
    private Material selectedMaterial;

    // パーティクルシステム
    public ParticleSystem particleSystem;
    // ブラシサイズ
    public float brushSize = 0.1f;

    // 現在の操作モード
    private enum Mode
    {
        Create, Move, Rotate, Scale, Boolean, Paint
    }
    private Mode currentMode;

    // 作成中のオブジェクト
    private GameObject creatingObject;
    // 選択中のオブジェクト
    private GameObject selectedObject;
    // ブーリアン演算のためのオブジェクト
    private GameObject booleanTargetObject;

    // オブジェクトのリスト
    private List<GameObject> objectList = new List<GameObject>();
    // UNDO履歴
    private Stack<UndoData> undoStack = new Stack<UndoData>();
    // REDO履歴
    private Stack<UndoData> redoStack = new Stack<UndoData>();

    // UNDOデータ構造
    private class UndoData
    {
        public GameObject targetObject;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    void Start()
    {
        // VR コントローラーのイベントを取得
        interactor = GetComponent<XRBaseInteractor>();
        interactor.selectEntered.AddListener(OnSelectEnter);
        interactor.selectExited.AddListener(OnSelectExit);

        // ファイル選択 UI のイベント登録
        fileSelectionPage.OnUiElementValueChanged += OnFileSelected;

        // アニメーション設定 UI のイベント登録
        animationFrequencySlider.OnUiElementValueChanged += OnAnimationFrequencyChange;
        animationAmplitudeSlider.OnUiElementValueChanged += OnAnimationAmplitudeChange;

        // 材質選択 UI のイベント登録
        for (int i = 0; i < materialElements.Length; i++)
        {
            materialElements[i].OnUiElementValueChanged += OnMaterialSelected;
        }

        // モデル保存用ファイルパスの設定
        saveFilePath = Path.Combine(Application.persistentDataPath, "molecule.json");

        // パーティクルシステムの設定
        particleSystem.maxParticles = 100; // パーティクル数の制限
        particleSystem.startLifetime = 0.5f; // パーティクルの寿命
    }

    void Update()
    {
        // VRコントローラーの入力に応じた処理
        switch (currentMode)
        {
            case Mode.Create:
                // トリガーを押すと、プリミティブを作成
                if (interactor.selectTarget != null && interactor.selectTarget.TryGetComponent(out XRBaseInteractable interactable))
                {
                    if (interactor.isSelectActive)
                    {
                        CreatePrimitive(interactable.transform.position);
                        interactor.selectTarget = null;
                    }
                }
                break;

            case Mode.Move:
                // オブジェクト移動
                if (selectedObject != null && interactor.isSelectActive)
                {
                    MoveObject(interactor.transform.position);
                }
                break;

            case Mode.Rotate:
                // オブジェクト回転
                if (selectedObject != null && interactor.isSelectActive)
                {
                    // ... (回転処理の実装)
                }
                break;

            case Mode.Scale:
                // オブジェクトスケール
                if (selectedObject != null && interactor.isSelectActive)
                {
                    // ... (スケール処理の実装)
                }
                break;

            case Mode.Boolean:
                // ブーリアン演算
                if (booleanTargetObject != null && interactor.isSelectActive)
                {
                    ExecuteBooleanOperation();
                    booleanTargetObject = null;
                }
                break;

            case Mode.Paint:
                // パーティクルによるペイント
                if (interactor.isSelectActive)
                {
                    CreateParticle(interactor.transform.position, selectedMaterial.color);
                }
                break;
        }

        // UNDO/REDO処理
        if (Input.GetKeyDown(KeyCode.Z) && Input.GetKey(KeyCode.LeftControl))
        {
            Undo();
        }
        else if (Input.GetKeyDown(KeyCode.Y) && Input.GetKey(KeyCode.LeftControl))
        {
            Redo();
        }
    }

    // ツール選択イベント
    private void OnToolChange(VRCUiPage page, VRCUiElement element)
    {
        // 選択されたツールに応じてモードを変更
        switch (element.name)
        {
            case "Create":
                currentMode = Mode.Create;
                break;

            case "Move":
                currentMode = Mode.Move;
                break;

            case "Rotate":
                currentMode = Mode.Rotate;
                break;

            case "Scale":
                currentMode = Mode.Scale;
                break;

            case "Boolean":
                currentMode = Mode.Boolean;
                break;

            case "Paint":
                currentMode = Mode.Paint;
                break;
        }
    }

    // アニメーション周波数変更イベント
    private void OnAnimationFrequencyChange(VRCUiPage page, VRCUiElement element)
    {
        animationFrequency = float.Parse(element.text);
    }

    // アニメーション振幅変更イベント
    private void OnAnimationAmplitudeChange(VRCUiPage page, VRCUiElement element)
    {
        animationAmplitude = float.Parse(element.text);
    }

    // 材質選択イベント
    private void OnMaterialSelected(VRCUiPage page, VRCUiElement element)
    {
        // 選択された材質を設定
        selectedMaterial = materials[int.Parse(element.name)];
    }

    // オブジェクト選択開始
    private void OnSelectEnter(SelectEnterEventArgs args)
    {
        if (currentMode == Mode.Move || currentMode == Mode.Rotate || currentMode == Mode.Scale)
        {
            selectedObject = args.interactable.gameObject;
        }
        else if (currentMode == Mode.Boolean)
        {
            if (booleanTargetObject == null)
            {
                booleanTargetObject = args.interactable.gameObject;
            }
        }
    }

    // オブジェクト選択解除
    private void OnSelectExit(SelectExitEventArgs args)
    {
        // ...
    }

    // プリミティブ作成
    private void CreatePrimitive(Vector3 position)
    {
        switch (selectedPrimitive)
        {
            case PrimitiveType.Sphere:
                creatingObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                break;

            case PrimitiveType.Cube:
                creatingObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                break;

            case PrimitiveType.Cylinder:
                creatingObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                break;
        }

        creatingObject.transform.position = position;
        objectList.Add(creatingObject);
        undoStack.Push(new UndoData { targetObject = creatingObject, position = position, rotation = creatingObject.transform.rotation, scale = creatingObject.transform.localScale });
    }

    // ブーリアン演算
    private void ExecuteBooleanOperation()
    {
        if (selectedObject == null || booleanTargetObject == null) return;

        // ブーリアン演算の実行 (Mesh の結合)
        // MeshFilter の取得
        MeshFilter selectedMeshFilter = selectedObject.GetComponent<MeshFilter>();
        MeshFilter targetMeshFilter = booleanTargetObject.GetComponent<MeshFilter>();
        // Mesh の取得
        Mesh selectedMesh = selectedMeshFilter.mesh;
        Mesh targetMesh = targetMeshFilter.mesh;
        // ブーリアン演算の実行
        switch (selectedBooleanOperation)
        {
            case BooleanOperationType.Union:
                selectedMesh.CombineMeshes(new[] { targetMesh }, true);
                break;

            case BooleanOperationType.Subtract:
                selectedMesh.CombineMeshes(new[] { targetMesh }, false);
                break;

            case BooleanOperationType.Intersect:
                // Mesh の交差処理 (複雑なため、別途実装が必要)
                break;
        }

        // booleanTargetObject を削除
        Destroy(booleanTargetObject);

        // UNDO 履歴に情報を追加
        undoStack.Push(new UndoData { targetObject = selectedObject, position = selectedObject.transform.position, rotation = selectedObject.transform.rotation, scale = selectedObject.transform.localScale });
    }

    // オブジェクト移動
    private void MoveObject(Vector3 position)
    {
        if (selectedObject != null)
        {
            undoStack.Push(new UndoData { targetObject = selectedObject, position = selectedObject.transform.position, rotation = selectedObject.transform.rotation, scale = selectedObject.transform.localScale });
            selectedObject.transform.position = position;
            redoStack.Clear();
        }
    }

    // オブジェクト回転
    private void RotateObject(Quaternion rotation)
    {
        if (selectedObject != null)
        {
            undoStack.Push(new UndoData { targetObject = selectedObject, position = selectedObject.transform.position, rotation = selectedObject.transform.rotation, scale = selectedObject.transform.localScale });
            selectedObject.transform.rotation = rotation;
            redoStack.Clear();
        }
    }

    // オブジェクトスケール
    private void ScaleObject(Vector3 scale)
    {
        if (selectedObject != null)
        {
            undoStack.Push(new UndoData { targetObject = selectedObject, position = selectedObject.transform.position, rotation = selectedObject.transform.rotation, scale = selectedObject.transform.localScale });
            selectedObject.transform.localScale = scale;
            redoStack.Clear();
        }
    }

    // UNDO機能
    private void Undo()
    {
        if (undoStack.Count > 0)
        {
            UndoData undoData = undoStack.Pop();
            undoData.targetObject.transform.position = undoData.position;
            undoData.targetObject.transform.rotation = undoData.rotation;
            undoData.targetObject.transform.localScale = undoData.scale;
            redoStack.Push(undoData);
        }
    }

    // REDO機能
    private void Redo()
    {
        if (redoStack.Count > 0)
        {
            UndoData redoData = redoStack.Pop();
            redoData.targetObject.transform.position = redoData.position;
            redoData.targetObject.transform.rotation = redoData.rotation;
            redoData.targetObject.transform.localScale = redoData.scale;
            undoStack.Push(redoData);
        }
    }

    // パーティクルの生成
    private void CreateParticle(Vector3 position, Color color)
    {
        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
        emitParams.position = position;
        emitParams.startColor = color;
        particleSystem.Emit(emitParams, 1);
    }

    // パーティクルの衝突判定
    private void OnParticleCollision(GameObject other)
    {
        // 衝突したオブジェクトに色を適用
        other.GetComponent<Renderer>().material = selectedMaterial;
    }

    // モデル保存機能
    private void SaveModel()
    {
        // 分子構造データを JSON 形式で保存
        // ...

        // VRC_EventHandler を使って、モデルデータをローカルに保存
        // ...
    }

    // モデル読み込み機能
    private void LoadModel()
    {
        // ローカルからモデルデータを読み込む
        // ...

        // 分子構造データからモデルを作成
        // ...
    }

    // 更新処理
    void Update()
    {
        // アニメーション処理
        foreach (AtomData atom in atomDataList)
        {
            // 調和振動子モデルによるアニメーション
            float displacement = animationAmplitude * Mathf.Sin(Time.time * animationFrequency);
            atom.position += new Vector3(0, displacement, 0);
        }

        // 分子モデルの更新
        CreateMoleculeModel();
    }
}

// 原子データ構造
[System.Serializable]
public struct AtomData
{
    public string element;
    public Vector3 position;
}

// 結合データ構造
[System.Serializable]
public struct BondData
{
    public int atomIndex1;
    public int atomIndex2;
}
