#include <bits/stdc++.h>

using namespace std;


typedef long long int ll;
typedef long double ld;
typedef vector<int> vi;
typedef vector<char> vc;
typedef pair<int,int> pii;

#define endl '\n'
#define _ ios::sync_with_stdio(0);cin.tie(0);

struct Agent {
    char id;
    int x;
    int y;
};

struct Step {
    int x;
    int y;
    int cost; // Costo real + heuristica

    bool operator<(const Step& other) const {
        return cost > other.cost;
    }
};

const vector<pii> moves = {
    {0, 1}, {1, 0}, {-1,0}, {0,-1}, {-1, 1}, {-1, -1}, {1, 1}, {1, -1}
};

int heuristic(int x1, int y1, int x2, int y2){
    int dx = abs(x1 - x2);
    int dy = abs(y1 - y2);
    return dx*dx + dy*dy;
}

// Algoritmo de A*
// Complejidad temporal: O(n*mlog(n*m)) en el peor caso
vector<pii> path_to_cell(Agent &agent, pii &destiny, vector<vector<char>> &grid){

    const ll MAX = 1e3;

    // Penalizamos pasar por casillas ya cosechadas y premiamos las casillas con cultivo

    const int CROP_COST     = 1;
    const int EMPTY_COST    = 2;
    const int RE_ENTER_COST = 10;


    int n = grid.size();
    int m = grid[0].size();

    int xf = destiny.first;
    int yf = destiny.second;

    vector<vector<ll>> costs(n, vector<ll>(m, MAX)); 
    priority_queue<Step> pq;
    
    pq.push({
        agent.x,
        agent.y,
        0
    });

    costs[agent.y][agent.x] = 0;

    while(!pq.empty()){

        Step c = pq.top();
        pq.pop();

        // Primer cultivo encontrado (cultivo óptimo)

        if(c.x == destiny.first && c.y == destiny.second){

          
            for(int y = 0; y < n; y++){
                for(int x = 0; x < m; x++) cout << costs[y][x] << " ";
                cout << endl;
            }

            // Reconstrucción de camino

            int x = c.x;
            int y = c.y;

            vector<pii> path;
            path.push_back({x, y});

            while (x != agent.x || y != agent.y) {

                for (auto &mov : moves) {

                    int nx = x + mov.first;
                    int ny = y + mov.second;

                    if (nx < 0 || nx >= m) continue;
                    if (ny < 0 || ny >= n) continue;

                    if (costs[ny][nx] == costs[y][x] - 
                        (grid[y][x] == 'W' ? CROP_COST :
                        grid[y][x] == '_' ? RE_ENTER_COST :
                        EMPTY_COST)) {

                        x = nx;
                        y = ny;

                        path.push_back({x, y});
                        break;
                    }
                }
            }

            reverse(path.begin(), path.end());

            return path;
        }

        for(auto &mov : moves){

            int nx = c.x + mov.first;
            int ny = c.y + mov.second;

            if(nx < 0 || nx >= m)  continue;
            if(ny < 0 || ny >= n)  continue;
            if(grid[ny][nx]== '#') continue; // No pasar por obstaculos

            int cost = 1;

            switch(grid[ny][nx]){
                case '.': cost = EMPTY_COST; // No hay cultivo (vacio)
                break;
                case 'W': cost = CROP_COST; // Hay cultivo
                break;
                case '_': cost = RE_ENTER_COST; // El cultivo ya fué cosechado
                break;
            }

            int newCost = costs[c.y][c.x] + cost;
            
            if(costs[ny][nx] > newCost){

                costs[ny][nx] = newCost;

                pq.push({
                    nx,
                    ny,
                    newCost + heuristic(nx,ny,xf,yf)
                });
            }
        }
    }

    return {};
}

int main(){



    vector<vector<char>> grid = {
        {'#','#','#','#','#','#','#','#','#','#','#','#'},
        {'#','.','_','_','.','.','#','.','.','W','.','#'},
        {'#','_','#','#','.','W','#','.','#','#','.','#'},
        {'#','_','#','.','.','.','.','.','.','#','W','#'},
        {'#','.','#','.','#','#','#','#','.','#','.','#'},
        {'#','.','_','.','W','.','W','.','.','.','.','#'},
        {'#','.','#','.','#','#','#','.','#','#','#','#'},
        {'#','W','.','.','#','_','_','.','.','W','.','#'},
        {'#','.','#','#','#','_','_','#','#','#','.','#'},
        {'#','#','#','#','#','#','#','#','#','#','#','#'}
    };

    Agent agent = {
        'A',
        1,  // x
        1   // y
    };


    pii destiny = {
        8, // x
        5  // y
    };

    vector<pii> path = path_to_cell(agent, destiny, grid);

    for(auto c : path) cout << c.first << " " << c.second << endl;
}
