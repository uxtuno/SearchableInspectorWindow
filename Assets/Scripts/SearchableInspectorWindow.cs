using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.IO;


/// <summary>
/// プロパティを検索可能なインスペクタ
/// </summary>
public class SearchableInspectorWindow : EditorWindow
{
	[MenuItem("Window/Searchable Inspector Window")]
	static void showWindow()
	{
		GetWindow<SearchableInspectorWindow>();
	}

	Vector2 scrollPosition;
	List<Editor> editors = new List<Editor>();

	void OnEnable()
	{
		Selection.selectionChanged += () => {
			rebuildEditorElements();
		};
	}

	void rebuildEditorElements()
	{
		if (!Selection.activeGameObject) {
			return;
		}

		editors.Clear();

		var editingComponents = new Dictionary<GameObject, Component>();

		Component[] activeObjectComponents = null;
		if (!!Selection.activeGameObject) {
			activeObjectComponents = Selection.activeGameObject.GetComponents<Component>();
		}

		if (activeObjectComponents == null) {
			return;
		}
		var sameTypeObjectList = new List<List<Object>>();

		for (int i = 0; i < activeObjectComponents.Length; ++i) {

			var typeSameIndex = activeObjectComponents
				.Where(item => item.GetType() == activeObjectComponents[i].GetType()) // 同一の型で絞り込み
				.Select((item, index) => new { item, index }) // インデックスを取得可能にする
				.First(item => item.item == activeObjectComponents[i]).index; // インデックス取得

			sameTypeObjectList.Add(new List<Object>());
			sameTypeObjectList[i].Add(activeObjectComponents[i]);

			foreach (GameObject selectObject in Selection.objects) {
				if (!selectObject || selectObject == Selection.activeGameObject) {
					continue;
				}

				var components = selectObject.GetComponents<Component>();

				var sameComponent = components
					.Where(item => item.GetType() == activeObjectComponents[i].GetType()) // 同一の型で絞り込み
					.Select((item, index) => new { item, index }) // インデックスを取得可能にする
					.Where(item => item.index == typeSameIndex) // 同一型のコンポーネントのリストから特定インデックスの要素を取り出す
					.FirstOrDefault()?.item; // 単一要素として取得

				if (!!sameComponent) {
					sameTypeObjectList[i].Add(sameComponent);
				}
			}
		}

		foreach (var components in sameTypeObjectList.Where(components => components.Count == Selection.objects.Length)) {
			editors.Add(Editor.CreateEditor(components.ToArray()));
		}

		Repaint();
	}

	private void OnGUI()
	{
		if (!Selection.activeGameObject) {
			return;
		}

		if (editors == null) {
			return;
		}

		var requiredUpdate = false; // 状態更新が必要か

		using (var scrollScope = new EditorGUILayout.ScrollViewScope(scrollPosition)) {
			foreach (var item in editors) {

				using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
					if (item.target == null) {
						requiredUpdate = true;
						continue;
					}

					var foldout = EditorGUILayout.InspectorTitlebar(true, item);

					if (item.GetType().CustomAttributes.Any(attribute => attribute.AttributeType == typeof(CanEditMultipleObjects))) {
						item.OnInspectorGUI();
					}
					else if (item.targets.Length == 1) {
						item.OnInspectorGUI();
					}
					else {
						EditorGUILayout.HelpBox("Multi-object editing not supported.", MessageType.Info);
					}

					if (!!changeCheck.changed) {
						rebuildEditorElements();
					}
				}
			}

			scrollPosition = scrollScope.scrollPosition;
		}

		if (Event.current.type == EventType.DragUpdated ||
			Event.current.type == EventType.DragPerform) {

			DragAndDrop.visualMode = DragAndDropVisualMode.Move;

			if (Event.current.type == EventType.DragPerform) {
				foreach (var item in DragAndDrop.paths) {
					var filename =  Path.GetFileNameWithoutExtension(item);
					var addType = System.Type.GetType(filename);

					foreach (GameObject selectObject in Selection.objects) {
						if (!selectObject) {
							continue;
						}

						selectObject.AddComponent(addType);
						Undo.RegisterCompleteObjectUndo(selectObject, "Add Component");
					}

				}

				requiredUpdate = true;
			}
		}

		if (!!requiredUpdate) {
			rebuildEditorElements();
		}
	}
}
