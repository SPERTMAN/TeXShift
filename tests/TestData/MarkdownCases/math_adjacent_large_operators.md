# 相邻大型运算符（issue #7）

## 原始复现

A1 完整公式：$\mathcal{L}_{track}=\sum_{j=1}^{M}\sum_{i=1}^{N}\vert{}\vert{}y_{j,i}-\hat{y}_{j,i}\vert{}\vert{}$

A2 两个求和：$\sum_{j=1}^{M}\sum_{i=1}^{N}$

A3 混合上下限：$\sum \sum_{i=1}$

## 操作数与分组

A4 混合有界与无界求和：$\mathcal{L}_{track}=\sum_{j=1}^{M}\sum\sum\sum_{i=1}^{N}\vert{}\vert{}y_{j,i}-\hat{y}_{j,i}\vert{}\vert{}$

A5 显示公式的连续求和：

$$
\sum_{j=1}^{M}\sum\sum\sum_{i=1}^{N}x_{j,i}
$$

B1 带操作数：$\sum_{j=1}^{M}\sum_{i=1}^{N}x_{j,i}$

B2 显式分组：$\sum_{j=1}^{M}{\sum_{i=1}^{N}{x_{j,i}}}$

B3 整体分组：${\sum_{j=1}^{M}\sum_{i=1}^{N}x_{j,i}}$

B4 三重求和：$\sum_j\sum_i\sum_k x$

B5 无上下限：$\sum\sum x$

B6 后接加法：$\sum_j\sum_i x+y$

B7 两组连续求和：$\sum_j\sum_i x+\sum_k\sum_l y$

## 其他大型运算符

C1 连乘：$\prod_{j=1}^{M}\prod_{i=1}^{N}x_{j,i}$

C2 积分：$\int_0^1\int_0^1 f(x,y)dxdy$

C3 混合运算符：$\sum_i\prod_j\int_0^1 f_{i,j}(x)dx$

C4 集合运算：$\bigcup_i\bigcap_j A_{i,j}$

## 嵌套结构与块级公式

$$\sum_{j=1}^{M}\sum_{i=1}^{N}x_{j,i}$$

$$\frac{\sum_j\sum_i x_{j,i}}{\sum_k y_k}$$

$$\sqrt{\sum_j\sum_i x_{j,i}}$$

$$\begin{pmatrix}\sum_j\sum_i x_{j,i} & 1 \\ 0 & \prod_j\prod_i y_{j,i}\end{pmatrix}$$

## 对照

D1 单个求和：$\sum_{j=1}^{M}x_j$

D2 加号隔开：$\sum_{j=1}^{M}x_j+\sum_{i=1}^{N}y_i$

D3 上下标：$x_{\sum_i a_i}^{\sum_j b_j}$

D4 极限：$\lim_{x\to 0}\lim_{y\to 0}f(x,y)$
