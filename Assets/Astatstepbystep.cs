using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A* usa G (custo real) + H (estimativa ao destino) para priorizar os nos.
// Agora o G tambem soma um custo de radiacao, entao o algoritmo passa a
// evitar celulas perigosas mesmo que isso signifique um caminho mais longo.
public class AStarStepByStep : MonoBehaviour
{
    private class Node
    {
        public int x, y;
        public int cost;
        public int radiationCost; // 0 = seguro, 1-4 = risco baixo/medio, 5+ = perigoso
        public int g = int.MaxValue;
        public int h;
        public Node parent;
        public State state;
        public int f { get { return g == int.MaxValue ? int.MaxValue : g + h; } }
    }

    private enum State { None, Open, Closed, Path }

    public float nodeSize = 1f;
    public Vector2Int size = new Vector2Int(10, 10);
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float obstacleHeight = 1f;
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform targetPoint;
    [SerializeField] private bool automatic;
    [SerializeField] private bool showGridGizmos = true;

    [Header("Radiacao")]
    [SerializeField] private List<Transform> radiationSources = new List<Transform>();
    [SerializeField] private int radiationRange = 4; // alcance em numero de nos (Manhattan)
    [SerializeField] private float radiationUpdateInterval = 0.5f;
    private float nextRadiationUpdateTime;

    [Header("Movimento do jogador")]
    [SerializeField] private Transform player;
    [SerializeField] private float moveSpeed = 4f;
    private readonly List<Node> currentPath = new List<Node>();
    private Coroutine moveRoutine;

    private Node[,] nodes;
    private readonly List<Node> openNodes = new List<Node>();
    private Node targetNode;
    private bool finished;

    void Start()
    {
        CreateGrid();
        // UpdateRadiationCosts() ja chama ResetSearch()+RunFullSearchAndMove()
        // no final (RecalculatePathDueToRadiation). Chamar de novo aqui geraria
        // uma segunda corrotina de movimento rodando junto com a primeira.
        UpdateRadiationCosts();
    }

    void Update()
    {
        // Espaco mostra uma expansao do algoritmo por vez (modo manual, passo a passo).
        if (Input.GetKey(KeyCode.Space)) StepSearch();
        if (Input.GetKeyDown(KeyCode.R)) ResetSearch();
        if (automatic && !finished) StepSearch();

        // A radiacao e recalculada periodicamente, e nao a cada frame,
        // para nao gastar processamento toda hora.
        if (Time.time >= nextRadiationUpdateTime)
        {
            nextRadiationUpdateTime = Time.time + radiationUpdateInterval;
            UpdateRadiationCosts();
        }
    }

    void CreateGrid()
    {
        nodes = new Node[size.x, size.y];
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                Node node = new Node();
                node.x = x;
                node.y = y;
                node.cost = IsWalkable(NodeToWorld(x, y)) ? 1 : int.MaxValue;
                nodes[x, y] = node;
            }
    }

    [ContextMenu("Reiniciar busca")]
    public void ResetSearch()
    {
        if (nodes == null || size.x <= 0 || size.y <= 0) return;
        openNodes.Clear();
        finished = false;

        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                nodes[x, y].g = int.MaxValue;
                nodes[x, y].h = 0;
                nodes[x, y].parent = null;
                // O estado "Path" tambem e limpo aqui: uma nova busca comeca do zero visualmente.
                nodes[x, y].state = State.None;
            }

        // Se o jogador ja existe e ja se moveu, a busca deve recomecar de onde
        // ele esta agora (nao do StartPoint original). Assim, quando a radiacao
        // muda e a rota e recalculada, a capsula continua para frente em vez de
        // ser puxada de volta ao ponto inicial da cena.
        Vector3 startWorldPos = (player != null)
            ? player.position
            : (startPoint != null ? startPoint.position : transform.position);
        Node start = WorldToNode(startWorldPos);
        targetNode = WorldToNode(targetPoint != null ? targetPoint.position : transform.position);
        if (start.cost == int.MaxValue || targetNode.cost == int.MaxValue)
        {
            finished = true;
            return;
        }

        start.g = 0;
        start.h = Distance(start, targetNode);
        start.state = State.Open;
        openNodes.Add(start);
    }

    [ContextMenu("Proximo passo")]
    public void StepSearch()
    {
        if (finished) return;

        // Regra central do A*: menor F, onde F = G + H.
        Node current = GetBestOpenNode();
        if (current == null)
        {
            finished = true;
            Debug.Log("Nao existe caminho.", this);
            return;
        }

        openNodes.Remove(current);
        current.state = State.Closed;
        if (current == targetNode)
        {
            finished = true;
            CreatePath();
            return;
        }

        foreach (Node neighbor in GetNeighbors(current))
        {
            if (neighbor.cost == int.MaxValue || neighbor.state == State.Closed) continue;

            // Uma celula com radiacao alta aumenta o G do vizinho, entao o A*
            // so vai escolher passar por ela se nao houver caminho seguro
            // equivalente (ou se o desvio custar caro demais). E assim que
            // a IA passa a "preferir" rotas mais seguras em vez das mais curtas.
            int newG = current.g + neighbor.cost + neighbor.radiationCost;
            if (newG >= neighbor.g) continue;

            neighbor.g = newG;
            // H e a distancia Manhattan: adequada porque nao ha diagonais.
            neighbor.h = Distance(neighbor, targetNode);
            neighbor.parent = current;
            if (neighbor.state != State.Open)
            {
                neighbor.state = State.Open;
                openNodes.Add(neighbor);
            }
        }
    }

    // Executa o A* inteiro de uma vez (sem esperar tecla), usado quando o
    // jogo precisa de uma rota pronta imediatamente: no inicio e sempre que
    // a radiacao muda o suficiente para justificar um recalculo.
    private void RunFullSearchAndMove()
    {
        int safetyLimit = size.x * size.y + 10; // evita loop infinito em caso de erro de grade
        int iterations = 0;
        while (!finished && iterations < safetyLimit)
        {
            StepSearch();
            iterations++;
        }

        StartMovement();
    }

    Node GetBestOpenNode()
    {
        Node best = null;
        foreach (Node node in openNodes)
        {
            // H desempata e deixa a direcao ao alvo mais visivel.
            if (best == null || node.f < best.f || (node.f == best.f && node.h < best.h)) best = node;
        }
        return best;
    }

    IEnumerable<Node> GetNeighbors(Node node)
    {
        if (node.x > 0) yield return nodes[node.x - 1, node.y];
        if (node.x + 1 < size.x) yield return nodes[node.x + 1, node.y];
        if (node.y > 0) yield return nodes[node.x, node.y - 1];
        if (node.y + 1 < size.y) yield return nodes[node.x, node.y + 1];
    }

    int Distance(Node a, Node b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    void CreatePath()
    {
        currentPath.Clear();
        for (Node node = targetNode; node != null; node = node.parent)
        {
            node.state = State.Path;
            currentPath.Add(node);
        }
        currentPath.Reverse(); // fica do inicio para o destino, pronto para o movimento
    }

    // ---------------------- RADIACAO ----------------------

    [ContextMenu("Atualizar radiacao")]
    public void UpdateRadiationCosts()
    {
        if (nodes == null) return;

        // 1) limpa o custo de radiacao de todos os nos, mantendo obstaculos intocados.
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                Node node = nodes[x, y];
                node.radiationCost = 0;
                // Reconfirma se a celula continua sendo um obstaculo fisico.
                node.cost = IsWalkable(NodeToWorld(x, y)) ? 1 : int.MaxValue;
            }

        // 2) aplica o perigo de cada fonte aos nos proximos (distancia Manhattan).
        foreach (Transform source in radiationSources)
        {
            if (source == null) continue;
            Node sourceNode = WorldToNode(source.position);

            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                {
                    Node node = nodes[x, y];
                    if (node.cost == int.MaxValue) continue; // obstaculo nao recebe radiacao

                    int distance = Mathf.Abs(node.x - sourceNode.x) + Mathf.Abs(node.y - sourceNode.y);
                    node.radiationCost += RadiationByDistance(distance);
                }
        }

        // 3) e 4) os obstaculos ja foram mantidos acima; agora e so recalcular a rota,
        // pois o perigo pode ter mudado o caminho mais seguro.
        RecalculatePathDueToRadiation();
    }

    // Curva de perigo: quanto mais perto da fonte, maior o custo extra.
    // Fora do alcance (radiationRange), a fonte nao influencia mais o no.
    private int RadiationByDistance(int distance)
    {
        if (distance == 0) return 8;
        if (distance == 1) return 5;
        if (distance == 2) return 3;
        if (distance == 3) return 1;
        return 0; // distancia 4 ou mais (ou fora do radiationRange) = sem risco extra
    }

    private void RecalculatePathDueToRadiation()
    {
        // Para o movimento atual do jogador, joga a busca fora e comeca de novo
        // com os novos custos de radiacao ja aplicados nos nos.
        StopMovement();
        ResetSearch();
        RunFullSearchAndMove();
    }

    // ---------------------- MOVIMENTO ----------------------

    private void StartMovement()
    {
        if (player == null || currentPath.Count == 0) return;
        moveRoutine = StartCoroutine(MoveAlongPath());
    }

    private void StopMovement()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }
    }

    private IEnumerator MoveAlongPath()
    {
        // Copia a rota atual: se ela for trocada no meio do caminho (nova radiacao),
        // esta coroutine e interrompida por StopMovement() antes de terminar.
        List<Node> path = new List<Node>(currentPath);

        foreach (Node node in path)
        {
            Vector3 destination = NodeToWorld(node.x, node.y);
            while (Vector3.Distance(player.position, destination) > 0.05f)
            {
                player.position = Vector3.MoveTowards(player.position, destination, moveSpeed * Time.deltaTime);
                yield return null;
            }
        }

        moveRoutine = null;
    }

    // ---------------------- GRID / MUNDO ----------------------

    Node WorldToNode(Vector3 world)
    {
        // Mantem a grade fixa na origem do mundo, como no AIMove.
        int x = Mathf.Clamp(Mathf.FloorToInt(world.x / nodeSize), 0, size.x - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(world.z / nodeSize), 0, size.y - 1);
        return nodes[x, y];
    }

    Vector3 NodeToWorld(int x, int y)
    {
        return new Vector3(x * nodeSize, 0, y * nodeSize);
    }

    bool IsWalkable(Vector3 center)
    {
        Vector3 halfExtents = new Vector3(nodeSize * .45f, obstacleHeight * .5f, nodeSize * .45f);
        return !Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleMask);
    }

    void OnDrawGizmos()
    {
        if (!showGridGizmos || size.x <= 0 || size.y <= 0) return;
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                Node node = nodes == null ? null : nodes[x, y];
                bool walkable = node == null ? IsWalkable(NodeToWorld(x, y)) : node.cost != int.MaxValue;
                Gizmos.color = GetColor(node, walkable);
                Gizmos.DrawCube(NodeToWorld(x, y), new Vector3(nodeSize * .9f, .05f, nodeSize * .9f));
            }
    }

    Color GetColor(Node node, bool walkable)
    {
        // O caminho final tem prioridade sobre a cor de radiacao, para continuar visivel.
        if (node != null && node.state == State.Path) return Color.cyan;

        if (!walkable) return Color.black; // obstaculo

        if (node == null) return new Color(0, 1, 0, .25f);

        // Faixas de risco definidas no enunciado:
        // 0 = seguro, 1-4 = baixo/medio, 5+ = perigoso.
        int radiation = node.radiationCost;
        if (radiation <= 0) return new Color(0, 1, 0, .25f);            // verde: seguro
        if (radiation <= 2) return new Color(1f, 1f, 0f, .45f);          // amarelo: risco baixo
        if (radiation <= 4) return new Color(1f, .55f, 0f, .55f);        // laranja: risco medio
        return new Color(.45f, 0f, 0f, .75f);                            // vermelho escuro: perigoso

        // Estados Open/Closed deixaram de ter cor propria: durante a busca automatica
        // eles ficam visualmente "por baixo" da cor de radiacao, que e a informacao
        // mais importante para o jogador entender o risco de cada celula.
    }
}