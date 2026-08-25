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

// Algoritmo de Multi-source BFS
// Complejidad temporal: O(n*m)
void distribute(vector<Agent> agents, vector<vector<char>> &grid){

    int n = grid.size();
    int m = grid[0].size();

    queue<Agent> q;
    const vector<pii> moves = {
        {0, 1}, {1, 0}, {-1,0}, {0,-1}, {-1, 1}, {-1, -1}, {1, 1}, {1, -1}
    };

    for(auto a : agents){
        q.push(a);
        grid[a.y][a.x] = 'X';
    }

    while(!q.empty()){

        Agent c = q.front();
        q.pop();

        for(auto &mov : moves){
            int nx = c.x + mov.first;
            int ny = c.y + mov.second;

            if(nx < 0 || nx >= m) continue;
            if(ny < 0 || ny >= n) continue;
            if(grid[ny][nx]!= '.') continue;

            grid[ny][nx] = c.id;
            q.push({ c.id, nx, ny });
        }
    }
}

int main(){


    const int n = 30;
    const int m = 30;

    vector<vector<char>> grid(n, vector<char>(m, '.'));
    vector<Agent> agents = {
        { '>', 1, 1 },
        { '#', 17, 8 },
        { '\\', 15, 15},
        { '/', 20, 20},
        { '_', 2, 25}
    };

    cout << "DISTRIBUCIÓN DE AREAS:" << endl;
    cout << endl;
    cout << "Agentes:" << endl;
    cout << endl;



    for(auto a : agents){
        cout << "( id: " << a.id << ", x: " << a.x << ", y: " << a.y << ")" << endl;
    }

    cout << endl;
    cout << "Huerto:" << endl;
    cout << endl;

	distribute(agents, grid);

    for(int y = 0; y < n; y++){
        for(int x = 0; x < m; x++) cout << grid[y][x] << " ";
        cout << endl;
    }
    cout << endl;
}
