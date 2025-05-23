# SPH Simulation

## 公式


### Poly6 核函数 - 用于密度计算
$$
W_{poly6}(\vec{r}, h) = \frac{315}{64 \pi h^9} \begin{cases} (h^2 - |\vec{r}|^2)^3 & 0 \le |\vec{r}| \le h \\ 0 & |\vec{r}| > h \end{cases}
$$

### Spiky 核函数 - 用于压力梯度计算

$$
\nabla W_{spiky}(\vec{r}, h) = -\frac{45}{\pi h^6} (h - |\vec{r}|)^2 \frac{\vec{r}}{|\vec{r}|}, \quad 0 < |\vec{r}| \le h
$$

### Viscosity 核函数 - 用于粘滞力计算

$$
\nabla^2 W_{viscosity}(\vec{r}, h) = \frac{45}{\pi h^6} (h - |\vec{r}|), \quad 0 \le |\vec{r}| \le h
$$

### 密度计算

$$
\rho_i = \sum_j m_j W_{poly6}(\vec{r}_i - \vec{r}_j, h)
$$

### 压力计算 (Tait 方程)

$$
P_i = B \left( \left( \frac{\rho_i}{\rho_0} \right)^\gamma - 1 \right)
$$

### 压力加速度

$$
\vec{a}_{pressure,i} = - \sum_j m_j \left( \frac{P_i}{\rho_i^2} + \frac{P_j}{\rho_j^2} \right) \nabla W_{spiky}(\vec{r}_i - \vec{r}_j, h)
$$

### 粘滞加速度

首先计算“力密度”项 $\vec{f}_{visc,i}$ (其中 $V_j = m_j / \rho_j$):
$$
\vec{f}_{visc,i} = \mu \sum_j V_j (\vec{v}_j - \vec{v}_i) \nabla^2 W_{viscosity}(\vec{r}_i - \vec{r}_j, h)
$$
然后计算粘滞加速度:
$$
\vec{a}_{viscosity,i} = \frac{\vec{f}_{visc,i}}{\rho_i}
$$
合并：
$$
\vec{a}_{viscosity,i} = \frac{\mu}{\rho_i} \sum_j \frac{m_j}{\rho_j} (\vec{v}_j - \vec{v}_i) \nabla^2 W_{viscosity}(\vec{r}_i - \vec{r}_j, h)
$$

### 总加速度

$$
\vec{a}_i = \vec{a}_{pressure,i} + \vec{a}_{viscosity,i} + \vec{a}_{extra,i}
$$

### 时间积分 (Semi-implicit Euler)

$$
\vec{v}_i(t + \Delta t) = \vec{v}_i(t) + \vec{a}_i(t) \Delta t
$$
$$
\vec{r}_i(t + \Delta t) = \vec{r}_i(t) + \vec{v}_i(t + \Delta t) \Delta t
$$

