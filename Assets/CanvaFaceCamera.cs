using UnityEngine;

public class CanvaFaceCamera: MonoBehaviour
{
    [Header("Configuração de Posição")]
    [SerializeField] private Vector3 offset = new Vector3(0, 1.5f, 0);

    private Transform enemyTransform;
    private Transform mainCameraTransform;

    void Start()
    {
        // Pega o pai direto. Se o Canvas for filho da bola, enemyTransform será a bola.
        enemyTransform = transform.parent;

        if (enemyTransform == null)
        {
            Debug.LogError($"[Erro] O Canvas '{gameObject.name}' NÃO está dentro de nenhum objeto! Ele precisa ser filho do Inimigo no Prefab.", gameObject);
        }

        // Busca a câmera principal
        if (Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
        else
        {
            // Alerta vermelho se a câmera estiver sem a Tag correta
            Debug.LogError("[Erro] Nenhuma câmera com a tag 'MainCamera' foi encontrada na cena! O texto não vai funcionar sem isso.", this);
        }
    }

    void LateUpdate()
    {
        // Se faltar a câmera ou o inimigo, o código não executa para evitar erros
        if (enemyTransform == null || mainCameraTransform == null) return;

        // 1. Força a posição absoluta no mundo (ignora o giro local do pai)
        transform.position = enemyTransform.position + offset;

        // 2. Força a rotação a olhar para a câmera
        transform.LookAt(transform.position + mainCameraTransform.rotation * Vector3.forward,
                         mainCameraTransform.rotation * Vector3.up);
    }
}
